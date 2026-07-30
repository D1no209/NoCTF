using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class RestoreChallengeHintEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post(
            "/admin/competitions/{competitionId}/challenges/"
            + "{competitionChallengeId}/hints/{hintId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRestoreCompetitionChallengeHint"));
        Summary(summary =>
        {
            summary.Summary = "Restores a soft-deleted competition challenge hint.";
            summary.Description = "Restores the Hint under its original CompetitionChallenge.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>>
        ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await hints.RestoreAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("hintId"),
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
