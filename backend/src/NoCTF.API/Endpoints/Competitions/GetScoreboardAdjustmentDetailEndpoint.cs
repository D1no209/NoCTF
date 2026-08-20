using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetScoreboardAdjustmentDetailRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }

    [QueryParam]
    public string? Cursor { get; set; }

    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class GetScoreboardAdjustmentDetailValidator
    : Validator<GetScoreboardAdjustmentDetailRequest>
{
    public GetScoreboardAdjustmentDetailValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record ScoreboardAdjustmentDetailResponse(
    Guid CompetitionId,
    Guid TeamId,
    int EntryCount,
    IReadOnlyList<ScoreboardActorResponse> Actors,
    IReadOnlyList<ScoreboardAdjustmentResponse> Items,
    string? NextCursor);

public sealed class GetScoreboardAdjustmentDetailEndpoint(
    ILeaderboardCache leaderboard,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<GetScoreboardAdjustmentDetailRequest,
        Results<Ok<ScoreboardAdjustmentDetailResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    private const string CursorEndpoint = "scoreboard.adjustment.detail";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard/teams/{teamId}/adjustments");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Get global scoreboard adjustments with signed cursor pagination.");
    }

    public override async Task<Results<Ok<ScoreboardAdjustmentDetailResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetScoreboardAdjustmentDetailRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        var visibility = await access.ResolveAsync(
            user.UserId, request.CompetitionId, DateTimeOffset.UtcNow, cancellationToken);
        if (visibility is null || visibility.DataScope == LeaderboardDataScope.Hidden)
            return TypedResults.NotFound();

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is null)
            return Processing(request.CompetitionId);

        var canObserve = user.UserId != Guid.Empty
            && await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken);
        var tracks = await getTracks.ExecuteAsync(
            request.CompetitionId,
            user.UserId == Guid.Empty ? null : user.UserId,
            canObserve,
            cancellationToken);
        if (tracks is null)
            return TypedResults.NotFound();
        projection = ScoreboardAudienceProjection.Filter(projection, canObserve);
        projection = ScoreboardAudienceProjection.FilterTracks(projection, tracks, canObserve);
        var team = projection.Snapshot.Teams.SingleOrDefault(item => item.TeamId == request.TeamId);
        if (team is null)
            return TypedResults.NotFound();

        var dataAsOf = projection.Snapshot.DataAsOf ?? projection.Snapshot.GeneratedAt;
        var scope = string.Join(':',
            request.CompetitionId.ToString("N"),
            user.UserId.ToString("N"),
            request.TeamId.ToString("N"),
            projection.Schema.Revision,
            projection.Snapshot.Version,
            dataAsOf.UtcTicks);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, scope, out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }

        var adjustments = projection.AdjustmentAllocations
            .Where(item => item.TeamId == request.TeamId
                && (position is null
                    || item.OccurredAt < position.CreatedAt
                    || item.OccurredAt == position.CreatedAt
                    && item.Id.CompareTo(position.Id) < 0))
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(request.Limit + 1)
            .ToArray();
        var page = adjustments.Take(request.Limit).ToArray();
        var actorIndexes = page
            .Where(item => item.ActorIndex is not null)
            .Select(item => item.ActorIndex!.Value)
            .ToHashSet();
        var actors = projection.DetailActors
            .Where(actor => actorIndexes.Contains(actor.Index))
            .OrderBy(actor => actor.Index)
            .Select(actor => new ScoreboardActorResponse(
                actor.Index,
                actor.UserId,
                actor.DisplayName))
            .ToArray();
        var items = page.Select(item => ScoreboardProtocolMapper.ToResponse(new ScoreboardAdjustment(
            item.Id,
            item.Kind,
            item.OccurredAt,
            item.ActorIndex,
            item.EarnedPoints,
            item.DeductedPoints,
            item.NetPoints))).ToArray();
        var nextCursor = adjustments.Length > request.Limit
            ? cursors.Encode(CursorEndpoint, scope, new(page[^1].OccurredAt, page[^1].Id))
            : null;
        return TypedResults.Ok(new ScoreboardAdjustmentDetailResponse(
            request.CompetitionId,
            request.TeamId,
            team.GlobalAdjustmentCount,
            actors,
            items,
            nextCursor));
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId,
            LeaderboardProjectionStateProtocol.Processing,
            statusUrl));
    }
}
