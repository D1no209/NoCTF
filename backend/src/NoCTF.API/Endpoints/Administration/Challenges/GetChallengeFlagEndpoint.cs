using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeFlagResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets a competition-scoped flag.");
    }

    public override async Task<Results<Ok<ChallengeFlagResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await flags.GetAsync(
            ChallengeFlagScope.Competition(competitionId, Route<Guid>("competitionChallengeId")),
            Route<Guid>("flagId"),
            actorId: null,
            isAdministrator: true,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeFlagMapping.ToResponse(result));
    }
}
