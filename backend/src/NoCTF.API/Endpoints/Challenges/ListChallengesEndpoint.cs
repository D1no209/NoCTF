using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Challenges.Management;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Visibility;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class ListChallengesRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class ListChallengesEndpoint(
    ListChallenges list,
    ICompetitionChallengeAudienceAccess audienceAccess,
    ICompetitionVisibilityAccess visibilityAccess,
    IUserContext user) : Endpoint<ListChallengesRequest, Results<Ok<ChallengeListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Lists published challenges for a competition.");
    }

    public override async Task<Results<Ok<ChallengeListResponse>, NotFound>> ExecuteAsync(
        ListChallengesRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await audienceAccess.CanReadAsync(user.UserId, competitionId, ct))
            return TypedResults.NotFound();
        var visibility = await visibilityAccess.ResolveAsync(
            user.UserId,
            competitionId,
            DateTimeOffset.UtcNow,
            ct);
        if (visibility is null
            || !ParticipantChallengeVisibilityPolicy.CanView(
                visibility.CompetitionStatus))
            return TypedResults.NotFound();
        var items = await list.ExecuteAsync(
            competitionId,
            includeUnpublished: false,
            includeDeleted: false,
            ct);
        return TypedResults.Ok(ChallengeMapper.ToListResponse(
            items,
            visibility.Visibility,
            visibility.DataScope));
    }
}
