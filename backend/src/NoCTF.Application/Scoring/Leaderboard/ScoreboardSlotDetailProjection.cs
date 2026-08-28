using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record ScoreboardSlotDetailPage(
    IReadOnlyList<ScoreboardActor> Actors,
    IReadOnlyList<ScoreboardSlotEntry> Entries,
    bool HasMore);

public static class ScoreboardSlotDetailProjection
{
    public static ScoreboardSlotDetailPage Project(
        ScoreboardProjection projection,
        Guid teamId,
        int columnIndex,
        ScoreboardScoreState scoreState,
        DateTimeOffset? settledAt,
        IReadOnlyList<ScoreboardSlotDetailFact> facts,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit)
    {
        var allocations = projection.EntryAllocations
            .Where(item => item.TeamId == teamId && item.ColumnIndex == columnIndex)
            .ToArray();
        var synthetic = allocations
            .Where(item => item.Source is null
                && (beforeCreatedAt is null
                    || item.Entry.OccurredAt < beforeCreatedAt
                    || item.Entry.OccurredAt == beforeCreatedAt
                    && item.Entry.Id.CompareTo(beforeId) < 0))
            .ToArray();
        var cachedActorsByIndex = projection.DetailActors.ToDictionary(actor => actor.Index);
        var pageActors = facts
            .Where(fact => fact.ActorUserId is not null)
            .Select(fact => new ActorIdentity(
                fact.ActorUserId!.Value,
                string.IsNullOrWhiteSpace(fact.ActorDisplayName) ? "-" : fact.ActorDisplayName))
            .Concat(synthetic
                .Where(item => item.Entry.ActorIndex is int index
                    && cachedActorsByIndex.ContainsKey(index))
                .Select(item =>
                {
                    var actor = cachedActorsByIndex[item.Entry.ActorIndex!.Value];
                    return new ActorIdentity(actor.UserId, actor.DisplayName);
                }))
            .GroupBy(actor => actor.UserId)
            .OrderBy(group => group.Key)
            .Select((group, index) => new ScoreboardActor(
                index,
                group.Key,
                group.Select(actor => actor.DisplayName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "-"))
            .ToArray();
        var actorIndexesByUserId = pageActors.ToDictionary(actor => actor.UserId, actor => actor.Index);
        var cachedActorUserIdsByIndex = projection.DetailActors
            .ToDictionary(actor => actor.Index, actor => actor.UserId);
        ScoreboardSlotEntry RemapSyntheticActor(ScoreboardSlotEntry entry) => entry with
        {
            ActorIndex = entry.ActorIndex is int actorIndex
                && cachedActorUserIdsByIndex.TryGetValue(actorIndex, out var actorUserId)
                && actorIndexesByUserId.TryGetValue(actorUserId, out var pageActorIndex)
                    ? pageActorIndex
                    : null
        };
        var visibleTeamIds = projection.Snapshot.Teams.Select(item => item.TeamId).ToHashSet();
        var entries = facts
            .Select(fact => MapFact(
                fact,
                allocations,
                projection.Schema.Mode,
                scoreState,
                settledAt,
                actorIndexesByUserId,
                visibleTeamIds))
            .Concat(synthetic.Select(item => RemapSyntheticActor(item.Entry)))
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(limit + 1)
            .ToArray();
        var page = entries.Take(limit).ToArray();
        var usedActorIndexes = page
            .Where(entry => entry.ActorIndex is not null)
            .Select(entry => entry.ActorIndex!.Value)
            .ToHashSet();
        var actorPairs = pageActors
            .Where(actor => usedActorIndexes.Contains(actor.Index))
            .OrderBy(actor => actor.Index)
            .Select((actor, index) => (OldIndex: actor.Index, Actor: actor with { Index = index }))
            .ToArray();
        var actorIndexMap = actorPairs.ToDictionary(pair => pair.OldIndex, pair => pair.Actor.Index);
        page = page.Select(entry => entry with
        {
            ActorIndex = entry.ActorIndex is int actorIndex
                && actorIndexMap.TryGetValue(actorIndex, out var mapped)
                    ? mapped
                    : null
        }).ToArray();
        return new(actorPairs.Select(pair => pair.Actor).ToArray(), page, entries.Length > limit);
    }

    private static ScoreboardSlotEntry MapFact(
        ScoreboardSlotDetailFact fact,
        IReadOnlyList<ScoreboardEntryAllocation> allocations,
        GameMode mode,
        ScoreboardScoreState scoreState,
        DateTimeOffset? settledAt,
        IReadOnlyDictionary<Guid, int> actorIndexes,
        IReadOnlySet<Guid> visibleTeamIds)
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
        var earned = pending ? null : allocation?.Source?.EarnedPointsPerOccurrence
            ?? (representative ? allocation?.Entry.EarnedPoints : null);
        var deducted = pending ? null : allocation?.Source?.DeductedPointsPerOccurrence
            ?? (representative ? allocation?.Entry.DeductedPoints : null);
        return new(
            fact.Id,
            allocation?.Entry.Kind ?? EntryKind(mode, fact.Kind),
            EntryOutcome(fact),
            fact.ActorUserId is Guid actorId && actorIndexes.TryGetValue(actorId, out var actorIndex)
                ? actorIndex
                : null,
            allocation?.Source?.VictimTeamId is Guid targetTeamId && visibleTeamIds.Contains(targetTeamId)
                ? targetTeamId
                : null,
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
        GameMode mode) =>
        source.Kind == fact.Kind
        && source.State == fact.State
        && source.Result == fact.Result
        && (!fact.ScoringIdentityKnown
            || source.FailureCode == fact.FailureCode && source.VictimTeamId == fact.VictimTeamId)
        && (mode is GameMode.Awdp or GameMode.Koh
            || source.ReferenceKind == fact.ReferenceKind && source.ReferenceId == fact.ReferenceId);

    private static ScoreboardEntryKind EntryKind(GameMode mode, GameplayFactKind kind) => kind switch
    {
        GameplayFactKind.FlagAttempt when mode is GameMode.Awd => ScoreboardEntryKind.Attack,
        GameplayFactKind.FlagAttempt => ScoreboardEntryKind.Solve,
        GameplayFactKind.HintUnlock => ScoreboardEntryKind.Hint,
        GameplayFactKind.BreakAttempt => ScoreboardEntryKind.Attack,
        GameplayFactKind.FixAttempt => ScoreboardEntryKind.Defense,
        GameplayFactKind.KohControlObservation => ScoreboardEntryKind.Control,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
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

    private sealed record ActorIdentity(Guid UserId, string DisplayName);
}
