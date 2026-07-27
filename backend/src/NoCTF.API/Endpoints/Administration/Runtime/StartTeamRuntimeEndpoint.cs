using FastEndpoints;
using Microsoft.AspNetCore.Http;
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
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/start");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminStartTeamRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Queues a team runtime start.";
            summary.Description = "Uses the normal mode, capacity, and runtime ownership policy for the selected team.";
        });
    }

    public override Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes, authorizer, user, RuntimeAction.Start,
            Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            Route<Guid>("teamId"), null, ct);
}

internal static class AdminRuntimeMutation
{
    public static async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>> ExecuteTeamAsync(
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
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Runtime action was rejected.",
                detail: "The runtime state or competition policy does not allow this action.");
        }
        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/competitions/{competitionId}/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}
