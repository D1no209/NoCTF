using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class StopTeamRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.TeamStop)));
        Description(builder => builder.WithName("AdminStopTeamRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Queues a team runtime stop.";
            summary.Description = "Stops the selected team's runtime through durable provider cleanup.";
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
            || runtime.TeamId != Route<Guid>("teamId"))
            return TypedResults.NotFound();
        return await AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes,
            RuntimeAction.Stop,
            competitionId,
            runtime.CompetitionChallengeId.Value,
            runtime.TeamId,
            null,
            timeProvider,
            ct);
    }
}
