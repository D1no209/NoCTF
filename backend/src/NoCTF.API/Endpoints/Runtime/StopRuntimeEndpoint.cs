using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class StopRuntimeRequest
{
    /// <summary>One-time verification token; optional when platform policy disables verification. Maximum 4096 characters.</summary>
    [FromHeader("X-NoCTF-Human-Verification", IsRequired = false, RemoveFromSchema = true)]
    public string? HumanVerificationToken { get; set; }

    [RouteParam]
    public Guid CompetitionId { get; set; }
    [RouteParam]
    public Guid CompetitionChallengeId { get; set; }
    [RouteParam]
    public Guid RuntimeInstanceId { get; set; }
}

public sealed class StopRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    GetPlayerRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<StopRuntimeRequest, Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {

        Description(builder => builder
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable));

        Delete("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(new HumanVerificationMetadata(HumanVerificationAction.Runtime)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.PlayerStop)));
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status409Conflict)
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => {
            summary.Params["X-NoCTF-Human-Verification"] = "One-time verification token, at most 4096 characters. Required only when the configured platform policy enables verification for this operation."; summary.Summary = "Queues a team runtime stop."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        StopRuntimeRequest request, CancellationToken ct)
    {
        var competitionId = request.CompetitionId;
        var competitionChallengeId = request.CompetitionChallengeId;
        var current = await get.ExecuteAsync(competitionId, competitionChallengeId, user.UserId, ct);
        if (current?.Id != request.RuntimeInstanceId)
            return TypedResults.NotFound();
        return await PlayerRuntimeMutation.ExecuteAsync(
            mutate, user, competitionId, competitionChallengeId,
            RuntimeAction.Stop, null, timeProvider, ct);
    }
}
