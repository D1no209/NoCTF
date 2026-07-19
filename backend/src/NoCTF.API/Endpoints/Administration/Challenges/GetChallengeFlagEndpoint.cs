using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetChallengeFlagEndpoint(
    GetChallengeFlag get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetChallengeFlagRequest,
        Results<Ok<ChallengeFlagSecretResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{challengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets a protected Challenge Flag for administrators.");
    }

    public override async Task<Results<Ok<ChallengeFlagSecretResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetChallengeFlagRequest request,
        CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.ChallengeId = Route<Guid>("challengeId");
        request.FlagId = Route<Guid>("flagId");
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();

        var flag = await get.ExecuteAsync(
            request.CompetitionId,
            request.ChallengeId,
            request.FlagId,
            ct);
        return flag is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeFlagMapper.ToResponse(flag));
    }
}
