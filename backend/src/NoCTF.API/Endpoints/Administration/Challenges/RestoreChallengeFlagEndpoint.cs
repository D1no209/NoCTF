using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class RestoreChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRestoreCompetitionChallengeFlag"));
        Summary(summary =>
        {
            summary.Summary = "Restores a deleted competition-scoped flag.";
            summary.Description = "Restores a soft-deleted flag owned by the selected competition challenge.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await flags.RestoreAsync(
            ChallengeFlagScope.Competition(
                competitionId,
                Route<Guid>("competitionChallengeId")),
            Route<Guid>("flagId"),
            actorId: null,
            isAdministrator: true,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
