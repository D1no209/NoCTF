using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class ListAdminChallengesEndpoint(
    ListChallenges list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Lists all competition challenges, including unpublished items.");
    }

    public override async Task<Results<Ok<ChallengeListResponse>, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var items = await list.ExecuteAsync(competitionId, includeUnpublished: true, ct);
        return TypedResults.Ok(ChallengeMapper.ToListResponse(items));
    }
}
