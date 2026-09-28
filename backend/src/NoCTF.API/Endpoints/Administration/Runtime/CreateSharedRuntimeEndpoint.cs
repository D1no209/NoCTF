using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class CreateSharedRuntimeRequest
{
    public Guid? ReplacesRuntimeId { get; set; }
}

public sealed class CreateSharedRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateSharedRuntimeRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.SharedCreate)));
        Description(builder => builder.WithName("AdminCreateSharedRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary => summary.Summary = "Creates or replaces a KoH shared runtime.");
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>,
        ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CreateSharedRuntimeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        if (request.ReplacesRuntimeId is Guid expected)
        {
            var current = await runtimes.GetAsync(competitionId, expected, ct);
            if (current?.CompetitionChallengeId != competitionChallengeId
                || current.TeamId is not null)
                return TypedResults.Conflict(new RuntimeConflictResponse(
                    "ReplacesRuntimeId does not identify the current shared Runtime."));
        }
        return await AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes,
            request.ReplacesRuntimeId is null ? RuntimeAction.Start : RuntimeAction.Reset,
            competitionId,
            competitionChallengeId,
            teamId: null,
            extension: null,
            timeProvider,
            ct);
    }
}
