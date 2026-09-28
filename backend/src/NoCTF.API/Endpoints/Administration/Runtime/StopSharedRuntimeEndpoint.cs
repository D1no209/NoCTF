using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class StopSharedRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.SharedStop)));
        Description(builder => builder.WithName("AdminStopSharedRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Queues a KoH shared runtime stop.";
            summary.Description = "Stops the shared hill through the durable runtime cleanup state machine.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var runtime = await runtimes.GetAsync(
            competitionId,
            Route<Guid>("runtimeInstanceId"),
            ct);
        if (runtime?.CompetitionChallengeId != Route<Guid>("competitionChallengeId")
            || runtime.TeamId is not null)
            return TypedResults.NotFound();
        return await AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes,
            RuntimeAction.Stop,
            competitionId,
            runtime.CompetitionChallengeId.Value,
            teamId: null,
            extension: null,
            timeProvider,
            ct);
    }
}
