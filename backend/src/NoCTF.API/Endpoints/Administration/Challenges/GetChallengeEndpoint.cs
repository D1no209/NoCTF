using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets challenge details, including unpublished challenges.");
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var item = await get.ExecuteAsync(
            competitionId,
            Route<Guid>("challengeId"),
            includeUnpublished: true,
            ct);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeMapper.ToResponse(item));
    }
}
