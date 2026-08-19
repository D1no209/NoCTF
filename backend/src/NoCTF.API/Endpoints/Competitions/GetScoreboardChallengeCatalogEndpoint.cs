using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Competitions;

public sealed record ScoreboardChallengeCatalogItemResponse(
    Guid Id,
    string Title,
    string Direction,
    string Category,
    int Order,
    bool Published);

public sealed record ScoreboardChallengeCatalogResponse(
    Guid CompetitionId,
    long Revision,
    IReadOnlyList<ScoreboardChallengeCatalogItemResponse> Items);

public sealed class GetScoreboardChallengeCatalogRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetScoreboardChallengeCatalogEndpoint(
    ILeaderboardCache leaderboard,
    ICompetitionVisibilityAccess access,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetScoreboardChallengeCatalogRequest, Results<Ok<ScoreboardChallengeCatalogResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard/challenges");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Get the low-frequency scoreboard challenge catalog.");
    }

    public override async Task<Results<Ok<ScoreboardChallengeCatalogResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound>> ExecuteAsync(
        GetScoreboardChallengeCatalogRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var visibility = await access.ResolveAsync(
            user.UserId, request.CompetitionId, DateTimeOffset.UtcNow, cancellationToken);
        if (visibility is null)
            return TypedResults.NotFound();
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
            return TypedResults.Ok(new ScoreboardChallengeCatalogResponse(request.CompetitionId, 0, []));
        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is null)
            return Processing(request.CompetitionId);
        var canObserve = user.UserId != Guid.Empty
            && await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken);
        var catalog = ScoreboardAudienceProjection.Filter(projection, canObserve).ChallengeCatalog;
        return TypedResults.Ok(new ScoreboardChallengeCatalogResponse(
            catalog.CompetitionId,
            catalog.Revision,
            catalog.Challenges.Select(item => new ScoreboardChallengeCatalogItemResponse(
                item.CompetitionChallengeId,
                item.Title,
                item.Direction,
                item.Category,
                item.Order,
                item.IsPublished)).ToArray()));
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard/challenges";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId, LeaderboardProjectionStateProtocol.Processing, statusUrl));
    }
}
