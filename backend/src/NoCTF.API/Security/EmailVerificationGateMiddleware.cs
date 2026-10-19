using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;

namespace NoCTF.API.Security;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<EmailVerificationProblemCode>))]
internal enum EmailVerificationProblemCode
{
    EmailVerificationRequired
}

public sealed class EmailVerificationGateMiddleware(RequestDelegate next)
{
    private static readonly object PendingKey = new();

    /// <summary>Records the gate decision read together with the bearer-token validation.</summary>
    internal static void SetPending(HttpContext context, bool pending) => context.Items[PendingKey] = pending;

    private static readonly HashSet<PathString> AllowedPaths =
    [
        new("/api/v1/auth/me"),
        new("/api/v1/auth/me/profile"),
        new("/api/v1/auth/me/avatar"),
        new("/api/v1/auth/me/school-identity"),
        new("/api/v1/auth/refresh"),
        new("/api/v1/auth/logout"),
        new("/api/v1/auth/logout-all"),
        new("/api/v1/auth/password"),
        new("/api/v1/auth/email-verification/request"),
        new("/api/v1/auth/email-verification/resend"),
        new("/api/v1/auth/email-verification/verify")
    ];

    public async Task InvokeAsync(
        HttpContext context,
        IEmailVerificationConfigurationStore configuration,
        IUserAuthenticationStore users)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !string.Equals(
                context.User.FindFirst("user_kind")?.Value,
                "Human",
                StringComparison.Ordinal)
            || AllowedPaths.Contains(context.Request.Path)
            || ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null))
        {
            await next(context);
            return;
        }

        if (context.Items.TryGetValue(PendingKey, out var known) && known is false)
        {
            await next(context);
            return;
        }
        if (known is not true)
        {
            var settings = await configuration.GetAsync(context.RequestAborted);
            if (!settings.Enabled)
            {
                await next(context);
                return;
            }
        }

        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        var profile = Guid.TryParse(subject, out var userId)
            ? await users.GetProfileAsync(userId, context.RequestAborted)
            : null;
        if (profile?.EmailVerified == true)
        {
            await next(context);
            return;
        }

        await ApiProblems.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: ApiMessages.Get(ApiMessageId.EmailVerificationGateMiddlewareTitleEmailVerificationRequired),
            detail: ApiMessages.Get(ApiMessageId.EmailVerificationGateMiddlewareDetailVerifyAccountEmailAddress),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = EmailVerificationProblemCode.EmailVerificationRequired
            }).ExecuteAsync(context);
    }
}
