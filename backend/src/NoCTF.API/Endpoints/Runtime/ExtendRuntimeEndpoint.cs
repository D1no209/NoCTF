using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class ExtendRuntimeRequest
{
    /// <summary>One-time verification token; optional when platform policy disables verification. Maximum 4096 characters.</summary>
    [FromHeader("X-NoCTF-Human-Verification", IsRequired = false, RemoveFromSchema = true)]
    public string? HumanVerificationToken { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ExtendRuntimeValidator : Validator<ExtendRuntimeRequest>
{
    public ExtendRuntimeValidator() =>
        RuleFor(request => request.ExpiresAt).NotEmpty();
}

public sealed class ExtendRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    GetPlayerRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ExtendRuntimeRequest, Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Description(builder => builder
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable));

        Patch("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(new HumanVerificationMetadata(HumanVerificationAction.Runtime)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.PlayerExtend)));
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status409Conflict)
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => {
            summary.Params["X-NoCTF-Human-Verification"] = "One-time verification token, at most 4096 characters. Required only when the configured platform policy enables verification for this operation."; summary.Summary = "Extends a running CTF runtime during its final ten minutes."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        ExtendRuntimeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var current = await get.ExecuteAsync(competitionId, competitionChallengeId, user.UserId, ct);
        if (current?.Id != Route<Guid>("runtimeInstanceId") || current.ExpiresAt is null)
            return TypedResults.NotFound();
        var extension = request.ExpiresAt - current.ExpiresAt.Value;
        var result = await mutate.ExecuteAsync(new RuntimeMutationCommand(
            competitionId,
            competitionChallengeId,
            user.UserId,
            RuntimeAction.Extend,
            extension,
            timeProvider.GetUtcNow()), ct);
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeNotFound or RuntimeMutationFailureCode.RuntimeActionUnsupported)
            return TypedResults.NotFound();
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeStateConflict
            or RuntimeMutationFailureCode.RuntimeExtensionTooEarly
            or RuntimeMutationFailureCode.RuntimeConflict)
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ApiMessages.Get(ApiMessageId.ExtendRuntimeTitleRuntimeExtensionConflictsState),
                detail: ApiMessages.For(result.FailureCode),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.FailureCode.Value.ToString()
                });
        if (!result.Succeeded)
        {
            if (result.FailureCode is { } failure)
            {
                NoCTF.Application.Observability.NoCtfTelemetry
                    .RecordRuntimeMutationFailure(
                        NoCTF.Application.Observability.RuntimeOperationMetricKind.PlayerExtend,
                        failure);
            }
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: ApiMessages.Get(ApiMessageId.ExtendRuntimeTitleRuntimeCouldExtended),
                detail: ApiMessages.For(result.FailureCode));
        }
        var accepted = RuntimeEndpointMapping.ToAccepted(result.Value!);
        return TypedResults.Accepted(accepted.StatusUrl, accepted);
    }
}
