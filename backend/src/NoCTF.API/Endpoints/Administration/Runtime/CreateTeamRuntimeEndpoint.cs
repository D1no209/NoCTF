using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class CreateTeamRuntimeRequest
{
    public Guid? ReplacesRuntimeId { get; set; }
}

public sealed class CreateTeamRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateTeamRuntimeRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtimes");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.TeamCreate)));
        Description(builder => builder.WithName("AdminCreateTeamRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary => summary.Summary = "Creates or replaces a team runtime.");
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>,
        ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CreateTeamRuntimeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var teamId = Route<Guid>("teamId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        if (request.ReplacesRuntimeId is Guid expected)
        {
            var current = await runtimes.GetAsync(competitionId, expected, ct);
            if (current?.CompetitionChallengeId != competitionChallengeId
                || current.TeamId != teamId)
                return TypedResults.Conflict(new RuntimeConflictResponse(
                    "ReplacesRuntimeId does not identify the selected team's current Runtime."));
        }

        return await AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes,
            request.ReplacesRuntimeId is null ? RuntimeAction.Start : RuntimeAction.Reset,
            competitionId,
            competitionChallengeId,
            teamId,
            null,
            timeProvider,
            ct);
    }
}

internal static class AdminRuntimeMutation
{
    public static async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>,
        ProblemHttpResult, ForbidHttpResult>> ExecuteTeamAsync(
        ManageAdminRuntimes runtimes,
        RuntimeAction action,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? teamId,
        TimeSpan? extension,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var result = await runtimes.MutateAsync(
            competitionId,
            competitionChallengeId,
            teamId,
            action,
            extension,
            timeProvider.GetUtcNow(),
            ct);
        if (result.Failure is RuntimeMutationFailure.NotFound or RuntimeMutationFailure.Unsupported)
            return TypedResults.NotFound();
        if (result.Failure == RuntimeMutationFailure.ExtensionTooEarly)
            return TypedResults.Conflict(new RuntimeConflictResponse(
                "Runtime renewal is available only during the final ten minutes before expiration."));
        if (result.Runtime is null)
            return TypedResults.Conflict(new RuntimeConflictResponse(
                "The Runtime state or competition policy does not allow this operation."));
        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}
