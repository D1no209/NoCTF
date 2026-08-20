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
using NoCTF.Domain.Gameplay;

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
}

public sealed class GetScoreboardSlotDetailValidator : Validator<GetScoreboardSlotDetailRequest>
{
    public GetScoreboardSlotDetailValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
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
    IScoreboardSlotDetailReader details,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    SignedKeysetCursor cursors,
    IUserContext user)
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
        var facts = await details.ReadAsync(new(
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
        var page = facts.Take(request.Limit).ToArray();
        var compactById = slot.Entries.ToDictionary(entry => entry.Id);
        var actors = page
            .Where(fact => fact.ActorUserId is not null
                && !string.IsNullOrWhiteSpace(fact.ActorDisplayName))
            .GroupBy(fact => fact.ActorUserId!.Value)
            .OrderBy(group => group.Key)
            .Select((group, index) => new ScoreboardActorResponse(
                index,
                group.Key,
                group.Select(fact => fact.ActorDisplayName!).First()))
            .ToArray();
        var actorIndexes = actors.ToDictionary(actor => actor.UserId, actor => actor.Index);
        var mapped = page.Select(fact =>
        {
            var entry = compactById.GetValueOrDefault(fact.Id)
                ?? ToEntry(fact, projection, slot, round);
            int? actorIndex = fact.ActorUserId is Guid actorId
                && actorIndexes.TryGetValue(actorId, out var index)
                    ? index
                    : null;
            return ScoreboardProtocolMapper.ToResponse(entry with { ActorIndex = actorIndex });
        }).ToArray();
        var nextCursor = facts.Count > request.Limit
            ? cursors.Encode(CursorEndpoint, scope, new(page[^1].OccurredAt, page[^1].Id))
            : null;
        return TypedResults.Ok(new ScoreboardSlotDetailResponse(
            request.CompetitionId,
            request.TeamId,
            request.ColumnIndex,
            Enum.Parse<ScoreboardScoreStateProtocol>(slot.ScoreState.ToString()),
            slot.EarnedPoints,
            slot.DeductedPoints,
            slot.NetPoints,
            slot.EntryCount,
            slot.Breakdowns.Select(item => new ScoreboardBreakdownResponse(
                Enum.Parse<ScoreboardBreakdownKindProtocol>(item.Kind.ToString()),
                item.SuccessfulCount,
                item.AttemptCount,
                item.EarnedPoints,
                item.DeductedPoints,
                item.NetPoints)).ToArray(),
            actors,
            mapped,
            nextCursor));
    }

    private static ScoreboardSlotEntry ToEntry(
        ScoreboardSlotDetailFact fact,
        ScoreboardProjection projection,
        ScoreboardSlot slot,
        ScoreboardRound? round)
    {
        var settled = slot.ScoreState == ScoreboardScoreState.Settled;
        return new(
            fact.Id,
            EntryKind(projection.Schema.Mode, fact.Kind),
            EntryOutcome(fact),
            null,
            fact.VictimTeamId,
            fact.OccurredAt,
            settled ? round?.SettledAt ?? projection.Snapshot.GeneratedAt : null,
            settled ? 0 : null,
            settled ? 0 : null,
            settled ? 0 : null);
    }

    private static ScoreboardEntryKind EntryKind(
        NoCTF.Domain.Competitions.GameMode mode,
        GameplayFactKind kind) => (mode, kind) switch
    {
        (_, GameplayFactKind.HintUnlock) => ScoreboardEntryKind.Hint,
        (_, GameplayFactKind.ManualAdjustment) => ScoreboardEntryKind.ManualAdjustment,
        (NoCTF.Domain.Competitions.GameMode.Ctf, _) => ScoreboardEntryKind.Solve,
        (NoCTF.Domain.Competitions.GameMode.Awdp, GameplayFactKind.FixAttempt) => ScoreboardEntryKind.Defense,
        (NoCTF.Domain.Competitions.GameMode.Awdp, _) => ScoreboardEntryKind.Attack,
        (NoCTF.Domain.Competitions.GameMode.Awd, GameplayFactKind.AwdServiceTransition) =>
            ScoreboardEntryKind.Availability,
        (NoCTF.Domain.Competitions.GameMode.Awd, _) => ScoreboardEntryKind.Attack,
        (NoCTF.Domain.Competitions.GameMode.Koh, _) => ScoreboardEntryKind.Control,
        _ => ScoreboardEntryKind.Penalty
    };

    private static ScoreboardEntryOutcome EntryOutcome(ScoreboardSlotDetailFact fact)
    {
        if (fact.State is GameplayFactState.Queued or GameplayFactState.Processing || fact.Result is null)
            return ScoreboardEntryOutcome.Pending;
        return fact.Result switch
        {
            GameplayFactResult.Correct or GameplayFactResult.Unlocked or GameplayFactResult.Applied
                or GameplayFactResult.ServiceUp or GameplayFactResult.Controlled
                => ScoreboardEntryOutcome.Succeeded,
            GameplayFactResult.Wrong or GameplayFactResult.AttemptsExhausted
                or GameplayFactResult.ServiceDown or GameplayFactResult.Uncontrolled
                => ScoreboardEntryOutcome.Failed,
            _ => ScoreboardEntryOutcome.Rejected
        };
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId, LeaderboardProjectionStateProtocol.Processing, statusUrl));
    }
}
