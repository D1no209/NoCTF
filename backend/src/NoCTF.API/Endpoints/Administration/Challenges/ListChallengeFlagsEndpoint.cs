using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class ListChallengeFlagsEndpoint(
    ListChallengeFlags list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeFlagSecretListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{challengeId}/flags");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Lists protected Challenge Flags for administrators.");
    }

    public override async Task<Results<Ok<ChallengeFlagSecretListResponse>, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var flags = await list.ExecuteAsync(competitionId, Route<Guid>("challengeId"), ct);
        return TypedResults.Ok(ChallengeFlagMapper.ToListResponse(flags));
    }
}
