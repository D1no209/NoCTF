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

public sealed class GetScoreboardSlotDetailRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public int ColumnIndex { get; set; }

    [QueryParam]
    public string? Cursor { get; set; }

    [QueryParam]
    public int Limit { get; set; } = 50;

    [QueryParam]
    public int? EndingRound { get; set; }
}

public sealed class GetScoreboardSlotDetailValidator : Validator<GetScoreboardSlotDetailRequest>
{
    public GetScoreboardSlotDetailValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
        RuleFor(request => request.EndingRound).GreaterThan(0).When(request => request.EndingRound is not null);
    }
}

public sealed record ScoreboardSlotDetailResponse(
    Guid CompetitionId,
    Guid TeamId,
    int ColumnIndex,
    ScoreboardScoreStateProtocol ScoreState,
    long? EarnedPoints,
    long? DeductedPoints,
    long? NetPoints,
    int EntryCount,
    IReadOnlyList<ScoreboardBreakdownResponse> Breakdown,
    IReadOnlyList<ScoreboardActorResponse> Actors,
    IReadOnlyList<ScoreboardEntryResponse> Items,
    string? NextCursor);

public sealed class GetScoreboardSlotDetailEndpoint(
    ILeaderboardCache leaderboard,
    ILeaderboardSnapshotFactory snapshots,
    IScoreboardDetailReader details,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    SignedKeysetCursor cursors,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetScoreboardSlotDetailRequest,
        Results<Ok<ScoreboardSlotDetailResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    private const string CursorEndpoint = "scoreboard.slot.detail";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/{columnIndex}");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Get one sparse scoreboard slot with signed cursor pagination.");
    }

    public override async Task<Results<Ok<ScoreboardSlotDetailResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetScoreboardSlotDetailRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        request.ColumnIndex = Route<int>("columnIndex");
        var visibility = await access.ResolveAsync(
            user.UserId, request.CompetitionId, timeProvider.GetUtcNow(), cancellationToken);
        if (visibility is null || visibility.DataScope == LeaderboardDataScope.Hidden)
            return TypedResults.NotFound();

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is not null
            && visibility.DataScope != LeaderboardDataScope.Frozen
            && request.EndingRound is int endingRound
            && visibility.GameMode is NoCTF.Domain.Competitions.GameMode.Awdp
                or NoCTF.Domain.Competitions.GameMode.Awd)
        {
            var sourceSnapshot = projection.Snapshot;
            var window = await snapshots.CreateScoreboardWindowAsync(
                request.CompetitionId,
                endingRound,
                sourceSnapshot.DataAsOf ?? sourceSnapshot.GeneratedAt,
                cancellationToken);
            projection = window is null
                ? null
                : ScoreboardAudienceProjection.PreserveSnapshotScope(window, sourceSnapshot);
        }
        if (projection is null)
            return Processing(request.CompetitionId);

        var canViewInternalTracks = user.UserId != Guid.Empty
            && await authorizer.CanJudgeAsync(user.UserId, request.CompetitionId, cancellationToken);
        var tracks = await getTracks.ExecuteAsync(
            request.CompetitionId,
            user.UserId == Guid.Empty ? null : user.UserId,
            canViewInternalTracks,
            includeInvitationCodes: false,
            cancellationToken);
        if (tracks is null)
            return TypedResults.NotFound();
        projection = ScoreboardAudienceProjection.ForPublishedChallenges(projection);
        projection = ScoreboardAudienceProjection.FilterTracks(
            projection,
            tracks,
            canViewInternalTracks);
        var column = projection.Schema.Columns.SingleOrDefault(item => item.Index == request.ColumnIndex);
        var team = projection.Snapshot.Teams.SingleOrDefault(item => item.TeamId == request.TeamId);
        var slot = team?.Slots.SingleOrDefault(item => item.ColumnIndex == request.ColumnIndex);
        if (column is null || team is null || slot is null)
            return TypedResults.NotFound();

        var dataAsOf = projection.Snapshot.DataAsOf ?? projection.Snapshot.GeneratedAt;
        var scope = string.Join(':',
            request.CompetitionId.ToString("N"),
            user.UserId.ToString("N"),
            request.TeamId.ToString("N"),
            request.ColumnIndex,
            projection.Schema.Revision,
            projection.Snapshot.Version,
            projection.Schema.RoundWindowStart,
            projection.Schema.RoundWindowEnd,
            dataAsOf.UtcTicks);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, scope, out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }

        var round = column.RoundId is Guid roundId
            ? projection.Schema.Rounds.Single(item => item.Id == roundId)
            : null;
        var facts = await details.ReadSlotAsync(new(
            request.CompetitionId,
            request.TeamId,
            column.CompetitionChallengeId,
            projection.Schema.Mode,
            column.RoundId,
            round?.StartAt,
            round?.EndAt,
            dataAsOf,
            position?.CreatedAt,
            position?.Id,
            request.Limit + 1), cancellationToken);
        var detailPage = ScoreboardSlotDetailProjection.Project(
            projection,
            request.TeamId,
            request.ColumnIndex,
            slot.ScoreState,
            round?.SettledAt,
            facts,
            position?.CreatedAt,
            position?.Id,
            request.Limit);
        var actors = detailPage.Actors
            .Select(actor => new ScoreboardActorResponse(
                actor.Index,
                actor.UserId,
                actor.DisplayName))
            .ToArray();
        var mapped = detailPage.Entries
            .Select(ScoreboardProtocolMapper.ToResponse)
            .ToArray();
        var nextCursor = detailPage.HasMore
            ? cursors.Encode(CursorEndpoint, scope, new(
                detailPage.Entries[^1].OccurredAt,
                detailPage.Entries[^1].Id))
            : null;
        return TypedResults.Ok(new ScoreboardSlotDetailResponse(
            request.CompetitionId,
            request.TeamId,
            request.ColumnIndex,
            ScoreboardProtocolMapper.ToProtocol(slot.ScoreState),
            slot.EarnedPoints,
            slot.DeductedPoints,
            slot.NetPoints,
            slot.EntryCount,
            slot.Breakdowns.Select(item => new ScoreboardBreakdownResponse(
                ScoreboardProtocolMapper.ToProtocol(item.Kind),
                item.SuccessfulCount,
                item.AttemptCount,
                item.EarnedPoints,
                item.DeductedPoints,
                item.NetPoints)).ToArray(),
            actors,
            mapped,
            nextCursor));
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId, LeaderboardProjectionStateProtocol.Processing, statusUrl));
    }
}
