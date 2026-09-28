using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class StopRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    GetPlayerRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
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
        Summary(summary => summary.Summary = "Queues a team runtime stop.");
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var current = await get.ExecuteAsync(competitionId, competitionChallengeId, user.UserId, ct);
        if (current?.Id != Route<Guid>("runtimeInstanceId"))
            return TypedResults.NotFound();
        return await PlayerRuntimeMutation.ExecuteAsync(
            mutate, user, competitionId, competitionChallengeId,
            RuntimeAction.Stop, null, timeProvider, ct);
    }
}
