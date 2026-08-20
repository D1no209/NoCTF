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
    IScoreboardDetailReader details,
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

        var allocations = projection.EntryAllocations
            .Where(allocation => allocation.TeamId == request.TeamId
                && allocation.ColumnIndex == request.ColumnIndex)
            .ToArray();
        var synthetic = allocations
            .Where(allocation => allocation.Source is null
                && (position is null
                    || allocation.Entry.OccurredAt < position.CreatedAt
                    || allocation.Entry.OccurredAt == position.CreatedAt
                    && allocation.Entry.Id.CompareTo(position.Id) < 0))
            .ToArray();
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
        var actorIndexesByUserId = projection.DetailActors
            .ToDictionary(actor => actor.UserId, actor => actor.Index);
        var entries = facts
            .Select(fact => MapFact(
                fact,
                allocations,
                projection.Schema.Mode,
                slot.ScoreState,
                round?.SettledAt,
                actorIndexesByUserId))
            .Concat(synthetic.Select(allocation => allocation.Entry))
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(request.Limit + 1)
            .ToArray();
        var page = entries.Take(request.Limit).ToArray();
        var actorIndexes = page
            .Where(entry => entry.ActorIndex is not null)
            .Select(entry => entry.ActorIndex!.Value)
            .ToHashSet();
        var actors = projection.DetailActors
            .Where(actor => actorIndexes.Contains(actor.Index))
            .OrderBy(actor => actor.Index)
            .Select(actor => new ScoreboardActorResponse(
                actor.Index,
                actor.UserId,
                actor.DisplayName))
            .ToArray();
        var mapped = page
            .Select(ScoreboardProtocolMapper.ToResponse)
            .ToArray();
        var nextCursor = entries.Length > request.Limit
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

    private static ScoreboardSlotEntry MapFact(
        ScoreboardSlotDetailFact fact,
        IReadOnlyList<ScoreboardEntryAllocation> allocations,
        NoCTF.Domain.Competitions.GameMode mode,
        ScoreboardScoreState scoreState,
        DateTimeOffset? settledAt,
        IReadOnlyDictionary<Guid, int> actorIndexes)
    {
        var candidates = allocations
            .Where(candidate => candidate.Source is { } source && Matches(source, fact, mode))
            .ToArray();
        var allocation = candidates.FirstOrDefault(candidate => candidate.Entry.Id == fact.Id)
            ?? (candidates.Length == 1 || candidates.Select(OutputIdentity).Distinct().Count() == 1
                ? candidates.FirstOrDefault()
                : null);
        var representative = allocation?.Entry.Id == fact.Id;
        var pending = scoreState == ScoreboardScoreState.Pending;
        var earned = pending
            ? null
            : allocation?.Source?.EarnedPointsPerOccurrence
                ?? (representative ? allocation?.Entry.EarnedPoints : null);
        var deducted = pending
            ? null
            : allocation?.Source?.DeductedPointsPerOccurrence
                ?? (representative ? allocation?.Entry.DeductedPoints : null);
        return new(
            fact.Id,
            allocation?.Entry.Kind ?? EntryKind(mode, fact.Kind),
            EntryOutcome(fact),
            fact.ActorUserId is Guid actorId && actorIndexes.TryGetValue(actorId, out var actorIndex)
                ? actorIndex
                : null,
            allocation?.Source?.VictimTeamId,
            fact.OccurredAt,
            pending ? null : settledAt,
            earned,
            deducted,
            pending || earned is null || deducted is null ? null : checked(earned.Value - deducted.Value),
            representative ? allocation?.Entry.Award : null,
            representative ? allocation?.Entry.AwardPoints ?? 0 : 0);
    }

    private static (ScoreboardEntryKind Kind, Guid? VictimTeamId, long? Earned, long? Deducted)
        OutputIdentity(ScoreboardEntryAllocation allocation) => (
            allocation.Entry.Kind,
            allocation.Source!.VictimTeamId,
            allocation.Source.EarnedPointsPerOccurrence,
            allocation.Source.DeductedPointsPerOccurrence);

    private static bool Matches(
        ScoreboardEntrySource source,
        ScoreboardSlotDetailFact fact,
        NoCTF.Domain.Competitions.GameMode mode) =>
        source.Kind == fact.Kind
        && source.State == fact.State
        && source.Result == fact.Result
        && source.ActorUserId == fact.ActorUserId
        && (!fact.ScoringIdentityKnown
            || source.FailureCode == fact.FailureCode && source.VictimTeamId == fact.VictimTeamId)
        && (mode is NoCTF.Domain.Competitions.GameMode.Awdp or NoCTF.Domain.Competitions.GameMode.Koh
            || source.ReferenceKind == fact.ReferenceKind && source.ReferenceId == fact.ReferenceId);

    private static ScoreboardEntryKind EntryKind(
        NoCTF.Domain.Competitions.GameMode mode,
        NoCTF.Domain.Gameplay.GameplayFactKind kind) => kind switch
        {
            NoCTF.Domain.Gameplay.GameplayFactKind.FlagAttempt when mode is NoCTF.Domain.Competitions.GameMode.Awd
                => ScoreboardEntryKind.Attack,
            NoCTF.Domain.Gameplay.GameplayFactKind.FlagAttempt => ScoreboardEntryKind.Solve,
            NoCTF.Domain.Gameplay.GameplayFactKind.HintUnlock => ScoreboardEntryKind.Hint,
            NoCTF.Domain.Gameplay.GameplayFactKind.BreakAttempt => ScoreboardEntryKind.Attack,
            NoCTF.Domain.Gameplay.GameplayFactKind.FixAttempt => ScoreboardEntryKind.Defense,
            NoCTF.Domain.Gameplay.GameplayFactKind.KohControlObservation => ScoreboardEntryKind.Control,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static ScoreboardEntryOutcome EntryOutcome(ScoreboardSlotDetailFact fact)
    {
        if (fact.State is NoCTF.Domain.Gameplay.GameplayFactState.Queued
            or NoCTF.Domain.Gameplay.GameplayFactState.Processing || fact.Result is null)
            return ScoreboardEntryOutcome.Pending;
        return fact.Result switch
        {
            NoCTF.Domain.Gameplay.GameplayFactResult.Correct
                or NoCTF.Domain.Gameplay.GameplayFactResult.Unlocked
                or NoCTF.Domain.Gameplay.GameplayFactResult.Applied
                or NoCTF.Domain.Gameplay.GameplayFactResult.ServiceUp
                or NoCTF.Domain.Gameplay.GameplayFactResult.Controlled
                => ScoreboardEntryOutcome.Succeeded,
            NoCTF.Domain.Gameplay.GameplayFactResult.Wrong
                or NoCTF.Domain.Gameplay.GameplayFactResult.AttemptsExhausted
                or NoCTF.Domain.Gameplay.GameplayFactResult.ServiceDown
                or NoCTF.Domain.Gameplay.GameplayFactResult.Uncontrolled
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
