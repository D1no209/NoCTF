using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class StartTeamRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/start");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Queues a team runtime start.");
    }

    public override Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes, authorizer, user, RuntimeAction.Start,
            Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            Route<Guid>("teamId"), null, ct);
}

internal static class AdminRuntimeMutation
{
    public static async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ForbidHttpResult>> ExecuteTeamAsync(
        ManageAdminRuntimes runtimes,
        ICompetitionModerationAuthorizer authorizer,
        IUserContext user,
        RuntimeAction action,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? teamId,
        TimeSpan? extension,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await runtimes.MutateAsync(
            competitionId, competitionChallengeId, teamId, action, extension,
            DateTimeOffset.UtcNow, ct);
        if (result.Failure is RuntimeMutationFailure.NotFound or RuntimeMutationFailure.Unsupported)
            return TypedResults.NotFound();
        if (result.Runtime is null)
            return TypedResults.Conflict();
        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/competitions/{competitionId}/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}
