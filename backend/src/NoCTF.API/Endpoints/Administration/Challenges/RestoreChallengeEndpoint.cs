using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class RestoreChallengeEndpoint(
    DeleteChallenge restore,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, Conflict>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/restore");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Restores a deleted competition challenge.";
            summary.Description = "Restores the competition link only; the global template is not modified.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await restore.RestoreAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            DateTimeOffset.UtcNow,
            ct);
        if (result.ErrorCode is "competition_not_found" or "competition_challenge_not_found")
            return TypedResults.NotFound();
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.Conflict();
    }
}
