using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Security;

public enum ProtectedEntry
{
    Authentication,
    Registration,
    SsoAuthentication,
    SsoCallback,
    PatchUpload,
    RuntimeCommand,
    ManualAdjustment,
    FlagSubmission
}
public sealed record ProtectedEntryMetadata(ProtectedEntry Entry);

public sealed class RequestAdmissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRequestAdmission admission,
        IRequestSourceAddress source, IOptions<RequestAdmissionOptions> options,
        NoCTF.Application.GameplayFacts.PatchUploads.IPatchUploadStore patchScope,
        NoCTF.Application.Messaging.PostCommitDispatchStatus dispatchStatus)
    {
        var entry = context.GetEndpoint()?.Metadata.GetMetadata<ProtectedEntryMetadata>()?.Entry;
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (entry is null && policy is not ("submission" or "question")) { await next(context); return; }
        var originalToken = context.RequestAborted;
        context.Response.OnStarting(() => {
            if (dispatchStatus.Pending)
                context.Response.Headers["X-NoCTF-Outbox-Delivery"] = "pending";
            return Task.CompletedTask;
        });
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (Guid.TryParse(userId, out var canonicalUserId)) userId = canonicalUserId.ToString("N");
        var ip = source.Address ?? "unknown";
        if (entry is ProtectedEntry.PatchUpload or ProtectedEntry.RuntimeCommand or ProtectedEntry.ManualAdjustment or ProtectedEntry.FlagSubmission
            && (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty))
        {
            await ApiProblems.Problem(statusCode: 400, title: ApiMessages.Get(ApiMessageId.RequestAdmissionMiddlewareTitleRequestAdmissionMiddleware),
                detail: ApiMessages.Get(ApiMessageId.RequestAdmissionMiddlewareDetailUuidIdempotencyKey),
                extensions: new Dictionary<string, object?> { ["code"] = "IdempotencyKeyRequired" }).ExecuteAsync(context);
            return;
        }
        var rates = new List<RateQuota>(); var slots = new List<ConcurrentQuota>();
        if (entry is ProtectedEntry.SsoAuthentication or ProtectedEntry.SsoCallback)
            rates.Add(new($"sso-ip:{ip}", options.Value.SsoIpPerMinute, 60));
        else if (entry is ProtectedEntry.Authentication or ProtectedEntry.Registration)
            rates.Add(new($"authentication-ip:{ip}", options.Value.AuthenticationIpPerMinute, 60));
        else
        {
            if (userId is null) { await next(context); return; } // Authentication supplies the 401 without consuming another user's quota.
            rates.Add(new($"entry-ip:{ip}", options.Value.SensitiveIpPerMinute, 60));
            rates.Add(new($"entry-user:{policy ?? entry.ToString()}:{userId}",
                policy == "submission"
                    ? options.Value.SubmissionPerUserPerMinute
                    : entry == ProtectedEntry.RuntimeCommand
                        ? options.Value.RuntimeCommandPerUserPerMinute
                        : 30,
                60));
        }
        if (entry == ProtectedEntry.PatchUpload)
        {
            slots.Add(new("patch-global", options.Value.PatchConcurrency));
            slots.Add(new($"patch-user:{userId}", options.Value.PatchPerUserConcurrency));
            var targetKey = Guid.TryParse(context.Request.RouteValues["runtimeInstanceId"]?.ToString(), out var canonicalTargetId)
                ? canonicalTargetId.ToString("N") : "invalid";
            slots.Add(new($"patch-target:{targetKey}", 1));
        }
        if (entry is ProtectedEntry.SsoAuthentication or ProtectedEntry.SsoCallback)
            slots.Add(new("sso-protocol-global", options.Value.SsoProtocolConcurrency));
        if (entry == ProtectedEntry.SsoCallback)
        {
            var providerKey = Guid.TryParse(
                context.Request.RouteValues["providerId"]?.ToString(),
                out var providerId)
                ? providerId.ToString("N")
                : "invalid";
            slots.Add(new($"sso-protocol-provider:{providerKey}",
                options.Value.SsoPerProviderConcurrency));
        }
        if (policy == "submission" || entry == ProtectedEntry.FlagSubmission)
        {
            slots.Add(new("submission-global", options.Value.SubmissionConcurrency));
            slots.Add(new($"submission-user:{userId}", options.Value.SubmissionPerUserConcurrency));
        }
        try
        {
            // Runs before FastEndpoints binds multipart files, so rejected uploads are not spooled.
            await using var lease = await admission.AcquireAsync(rates, slots, originalToken);
            if (entry == ProtectedEntry.PatchUpload)
            {
            if (!Guid.TryParse(userId, out var actorId)
                || !Guid.TryParse(context.Request.RouteValues["competitionId"]?.ToString(), out var competitionId)
                || !Guid.TryParse(context.Request.RouteValues["competitionChallengeId"]?.ToString(), out var challengeId)
                || !Guid.TryParse(context.Request.RouteValues["runtimeInstanceId"]?.ToString(), out var targetId)
                || !await patchScope.CanAccessTargetAsync(competitionId, challengeId, targetId, actorId, originalToken))
            {
                await ApiProblems.Problem(statusCode: 403, title: ApiMessages.Get(ApiMessageId.RequestAdmissionMiddlewareTitlePatch),
                    detail: ApiMessages.Get(ApiMessageId.RequestAdmissionMiddlewareDetailRequestAdmissionMiddleware)).ExecuteAsync(context);
                return;
            }
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
            deadline.CancelAfter(entry == ProtectedEntry.PatchUpload ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(45));
            context.RequestAborted = deadline.Token;
            try { await next(context); }
            catch (OperationCanceledException) when (!originalToken.IsCancellationRequested && !context.Response.HasStarted)
            {
                await WriteFailure(context, new(AdmissionFailure.DependencyUnavailable));
            }
        }
        catch (AdmissionRejectedException failure) when (!context.Response.HasStarted) { await WriteFailure(context, failure); }
        finally { context.RequestAborted = originalToken; }
    }
    internal static Task WriteFailure(HttpContext context, AdmissionRejectedException failure)
    {
        context.Response.Headers.RetryAfter = failure.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ApiProblems.Problem(statusCode: failure.Failure == AdmissionFailure.DependencyUnavailable ? 503 : 429,
            title: ApiMessages.For(failure.Failure),
            detail: ApiMessages.For(failure.Failure),
            extensions: new Dictionary<string, object?> { ["code"] = failure.Failure.ToString() }).ExecuteAsync(context);
    }
}
