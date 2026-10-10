using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Registration;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>
/// Produces the bounded fact summaries used by the stable leaderboard snapshot. Raw immutable facts
/// remain available through the signed-cursor detail reader; the maintenance projection never loads
/// the complete competition history.
/// </summary>
internal static class LeaderboardFactProjectionReader
{
    public static async Task<LeaderboardFactProjectionRows> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        GameMode mode,
        CompetitionStatus competitionStatus,
        CompetitionModeConfiguration? competitionConfiguration,
        DateTimeOffset? competitionStart,
        DateTimeOffset? competitionEnd,
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset projectedAt,
        IReadOnlyDictionary<Guid, long> hintCosts,
        IReadOnlyList<LeaderboardTeamFact> teams,
        int? endingRound,
        IReadOnlyList<LeaderboardAwdRoundFact>? awdWindowRounds,
        IReadOnlyList<LeaderboardChallengeFact> challenges,
        CancellationToken ct,
        NoCTF.Application.Challenges.Timing.ChallengeTimingProjectionOverride? timingOverride = null)
    {
        var query = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId && fact.OccurredAt <= projectedAt);
        var displayQuery = query;
        var overrideId = timingOverride?.CompetitionChallengeId;
        var overrideOpening = timingOverride?.Timing.AutoOpenAt;
        var overrideScoringEnd = timingOverride?.Timing.ScoringEndsAt;
        var overrideSubmissionEnd = timingOverride?.Timing.SubmissionDeadlineAt;
        query = query.Where(fact => fact.Kind == GameplayFactKind.ManualAdjustment || fact.Kind == GameplayFactKind.HintUnlock
            || fact.Kind == GameplayFactKind.AwdServiceTransition
            || fact.CompetitionChallengeId == overrideId
                && (overrideOpening == null || fact.OccurredAt >= overrideOpening)
                && (overrideScoringEnd == null || fact.OccurredAt < overrideScoringEnd)
                && (fact.Kind == GameplayFactKind.KohControlObservation || overrideSubmissionEnd == null || fact.OccurredAt < overrideSubmissionEnd)
            || db.CompetitionChallenges.Any(challenge => challenge.Id == fact.CompetitionChallengeId && challenge.Id != overrideId
                && (challenge.AutoOpenAt == null || fact.OccurredAt >= challenge.AutoOpenAt)
                && (challenge.ScoringEndsAt == null || fact.OccurredAt < challenge.ScoringEndsAt)
                && (fact.Kind == GameplayFactKind.KohControlObservation || challenge.SubmissionDeadlineAt == null
                    || fact.OccurredAt < challenge.SubmissionDeadlineAt)));
        if (mode == GameMode.Awdp)
        {
            var window = BuildAwdpWindow(
                competitionConfiguration,
                competitionStart,
                lifecycle,
                projectedAt,
                competitionStatus,
                endingRound);
            var aggregateRows = await ReadAwdpAggregateAsync(
                query,
                window.SettledPenaltyCutoff,
                window.IncludePenaltyCutoff,
                ct);
            var windowRows = await ReadAwdpWindowAsync(
                displayQuery.Where(fact => fact.OccurredAt >= window.StartAt
                    && fact.OccurredAt < window.EndAt),
                BuildRoundSelector(competitionStart, lifecycle, window),
                ct);
            var carryRows = aggregateRows.Where(row =>
                row.Kind == GameplayFactKind.ManualAdjustment
                || row.Result == GameplayFactResult.Correct && row.OccurredAt < window.StartAt);
            return new(
                Map(aggregateRows, hintCosts),
                Map(windowRows.Concat(carryRows)
                    .GroupBy(row => row.Id)
                    .Select(group => group.First()), hintCosts));
        }

        if (mode == GameMode.Awd)
        {
            var aggregates = await ReadAwdAggregatesAsync(
                db,
                competitionId,
                competitionConfiguration,
                challenges,
                teams,
                projectedAt,
                ct, timingOverride);
            var aggregateRows = await ReadAwdManualAdjustmentsAsync(query, ct);
            var windowRows = await ReadAwdWindowAsync(displayQuery, awdWindowRounds ?? [], ct);
            if (awdWindowRounds is { Count: > 0 } && await db.WriteUpUnlockReceipts.AsNoTracking()
                .AnyAsync(x => x.CompetitionId == competitionId && x.UnlockedAt <= projectedAt, ct))
            {
                var carry = await ReadAwdAggregatesAsync(db, competitionId, competitionConfiguration, challenges,
                    teams, awdWindowRounds.Min(x => x.StartsAt), ct, timingOverride);
                var prior = carry.ToDictionary(x => (x.TeamId, x.CompetitionChallengeId), x => x.PositivePoints);
                aggregates = aggregates.Select(x => x with { PositivePointsBeforeWindow = prior.GetValueOrDefault((x.TeamId, x.CompetitionChallengeId)) }).ToArray();
            }
            return new(
                Map(aggregateRows, hintCosts),
                Map(windowRows.Concat(aggregateRows), hintCosts),
                aggregates);
        }

        var rows = mode switch
        {
            GameMode.Ctf => await ReadCtfAsync(
                query,
                CompetitionOfficialWindow.Resolve(
                    competitionStart!.Value,
                    competitionEnd!.Value,
                    lifecycle),
                ct),
            GameMode.Koh => await ReadKohAsync(query, teams, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        var mapped = Map(rows, hintCosts);
        if (mode == GameMode.Ctf)
        {
            var displayed = await ReadCtfAsync(displayQuery,
                CompetitionOfficialWindow.Resolve(competitionStart!.Value, competitionEnd!.Value, lifecycle), ct);
            return new(mapped, Map(displayed, hintCosts), null);
        }
        return new(mapped, mapped, null);
    }

    private static IReadOnlyList<LeaderboardGameplayFact> Map(
        IEnumerable<FactSummary> rows,
        IReadOnlyDictionary<Guid, long> hintCosts) => rows.Select(row => new LeaderboardGameplayFact(
                row.Id,
                row.TeamId,
                row.CompetitionChallengeId,
                row.Kind,
                row.OccurredAt,
                row.State,
                row.Result,
                row.FailureCode,
                row.ReferenceKind,
                row.ReferenceId,
                row.VictimTeamId,
                null,
                row.Value,
                row.ReferenceKind == GameplayFactReferenceKind.Hint && row.ReferenceId is Guid hintId
                    ? hintCosts.GetValueOrDefault(hintId)
                    : null,
                row.ActorUserId,
                row.Multiplicity,
                row.LastOccurredAt))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToArray();

    private static Task<List<FactSummary>> ReadCtfAsync(
        IQueryable<GameplayFact> query,
        CompetitionOfficialWindow officialWindow,
        CancellationToken ct) => query
        .Where(fact => fact.Kind == GameplayFactKind.ManualAdjustment
            || (fact.Kind == GameplayFactKind.FlagAttempt
                || fact.Kind == GameplayFactKind.FixAttempt
                || fact.Kind == GameplayFactKind.HintUnlock)
            && fact.OccurredAt >= officialWindow.StartAt
            && fact.OccurredAt < officialWindow.EndAt)
        .GroupBy(fact => new
        {
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.State,
            fact.Result,
            fact.FailureCode,
            ReferenceKind = fact.Kind == GameplayFactKind.HintUnlock ? fact.ReferenceKind : null,
            ReferenceId = fact.Kind == GameplayFactKind.HintUnlock ? fact.ReferenceId : null,
            fact.VictimTeamId,
            fact.ActorUserId,
            Value = fact.Kind == GameplayFactKind.ManualAdjustment ? fact.Value : null
        })
        .Select(group => new FactSummary(
            group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id).Select(fact => fact.Id).First(),
            group.Key.TeamId,
            group.Key.CompetitionChallengeId,
            group.Key.Kind,
            group.Min(fact => fact.OccurredAt),
            group.Key.State,
            group.Key.Result,
            group.Key.FailureCode,
            group.Key.ReferenceKind,
            group.Key.ReferenceId,
            group.Key.VictimTeamId,
            group.Key.ActorUserId,
            group.Key.Value,
            group.Count(),
            group.Max(fact => fact.OccurredAt)))
        .ToListAsync(ct);

    private static Task<List<FactSummary>> ReadAwdManualAdjustmentsAsync(
        IQueryable<GameplayFact> query,
        CancellationToken ct) => query
            .Where(fact => fact.Kind == GameplayFactKind.ManualAdjustment)
            .GroupBy(fact => new
            {
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ReferenceKind,
                fact.ReferenceId,
                fact.VictimTeamId,
                fact.ActorUserId,
                Value = fact.Kind == GameplayFactKind.ManualAdjustment ? fact.Value : null
            })
            .Select(group => new FactSummary(
                group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id).Select(fact => fact.Id).First(),
                group.Key.TeamId,
                group.Key.CompetitionChallengeId,
                group.Key.Kind,
                group.Min(fact => fact.OccurredAt),
                group.Key.State,
                group.Key.Result,
                group.Key.FailureCode,
                group.Key.ReferenceKind,
                group.Key.ReferenceId,
                group.Key.VictimTeamId,
                group.Key.ActorUserId,
                group.Key.Value,
                group.Count(),
                group.Max(fact => fact.OccurredAt)))
            .ToListAsync(ct);

    private static async Task<IReadOnlyList<LeaderboardAwdAggregateFact>> ReadAwdAggregatesAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CompetitionModeConfiguration? competitionConfiguration,
        IReadOnlyList<LeaderboardChallengeFact> challenges,
        IReadOnlyList<LeaderboardTeamFact> teams,
        DateTimeOffset projectedAt,
        CancellationToken ct,
        NoCTF.Application.Challenges.Timing.ChallengeTimingProjectionOverride? timingOverride = null)
    {
        var overrideId = timingOverride?.CompetitionChallengeId;
        var overrideOpening = timingOverride?.Timing.AutoOpenAt;
        var overrideScoringEnd = timingOverride?.Timing.ScoringEndsAt;
        var overrideSubmissionEnd = timingOverride?.Timing.SubmissionDeadlineAt;
        var activeTeamIds = teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .Select(team => team.Id)
            .ToArray();
        var competitiveTeamIds = teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToArray();
        var activeChallengeIds = challenges
            .Where(challenge => !challenge.IsDeleted)
            .Select(challenge => challenge.Id)
            .ToArray();
        var accumulators = activeTeamIds
            .SelectMany(teamId => activeChallengeIds.Select(challengeId => new
            {
                Key = (TeamId: teamId, ChallengeId: challengeId),
                Value = new AwdAggregateAccumulator()
            }))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (accumulators.Count == 0)
            return [];

        var configuration = competitionConfiguration is AwdCompetitionModeConfiguration awd
            ? TypedGameModeConfiguration.Awd(awd)
            : AwdConfiguration.Default;
        var settingsByChallenge = challenges
            .Where(challenge => activeChallengeIds.Contains(challenge.Id))
            .ToDictionary(
                challenge => challenge.Id,
                challenge => Effective(configuration, challenge.Rules));
        var attackFacts = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.OccurredAt <= projectedAt
                && activeChallengeIds.Contains(fact.CompetitionChallengeId)
                && fact.TeamId != null
                && competitiveTeamIds.Contains(fact.TeamId.Value)
                && fact.VictimTeamId != null
                && competitiveTeamIds.Contains(fact.VictimTeamId.Value)
                && fact.VictimTeamId != fact.TeamId
                && fact.Kind == GameplayFactKind.FlagAttempt
                && (fact.Result == GameplayFactResult.Correct || fact.Result == GameplayFactResult.RightButDue)
                && (fact.CompetitionChallengeId == overrideId
                    && (overrideOpening == null || fact.OccurredAt >= overrideOpening)
                    && (overrideScoringEnd == null || fact.OccurredAt < overrideScoringEnd)
                    && (overrideSubmissionEnd == null || fact.OccurredAt < overrideSubmissionEnd)
                    || db.CompetitionChallenges.Any(challenge => challenge.Id == fact.CompetitionChallengeId && challenge.Id != overrideId
                    && (challenge.AutoOpenAt == null || fact.OccurredAt >= challenge.AutoOpenAt)
                    && (challenge.ScoringEndsAt == null || fact.OccurredAt < challenge.ScoringEndsAt)
                    && (challenge.SubmissionDeadlineAt == null || fact.OccurredAt < challenge.SubmissionDeadlineAt)))
                && fact.ReferenceKind == GameplayFactReferenceKind.AwdRound
                && fact.ReferenceId != null);
        var attackGroups = attackFacts
            .GroupBy(fact => new
            {
                fact.CompetitionChallengeId,
                AttackerId = fact.TeamId!.Value,
                VictimId = fact.VictimTeamId!.Value,
                RoundId = fact.ReferenceId!.Value
            })
            .Select(group => new
            {
                group.Key.CompetitionChallengeId,
                group.Key.AttackerId,
                group.Key.VictimId,
                group.Key.RoundId,
                LastAttackAt = group.Max(fact => fact.OccurredAt)
            })
            .OrderBy(attack => attack.CompetitionChallengeId)
            .ThenBy(attack => attack.VictimId)
            .ThenBy(attack => attack.RoundId)
            .ThenBy(attack => attack.AttackerId);
        Guid? currentChallengeId = null;
        Guid? currentVictimId = null;
        Guid? currentRoundId = null;
        var poolAttackers = new List<AwdAttackTuple>();

        void ApplyAttackPool()
        {
            if (poolAttackers.Count == 0 || currentChallengeId is null || currentVictimId is null)
                return;

            var settings = settingsByChallenge[currentChallengeId.Value];
            if (accumulators.TryGetValue((currentVictimId.Value, currentChallengeId.Value), out var victim))
                victim.Score = checked(victim.Score - settings.VictimDefensePoolPoints);

            var reward = settings.AttackRewardMode == AttackRewardMode.FixedPerAttack
                ? settings.AttackPoints
                : settings.VictimDefensePoolPoints / poolAttackers.Count;
            foreach (var attack in poolAttackers)
            {
                if (!accumulators.TryGetValue((attack.AttackerId, currentChallengeId.Value), out var accumulator))
                    continue;
                accumulator.Score = checked(accumulator.Score + reward);
                accumulator.AttackPoints = checked(accumulator.AttackPoints + reward);
                accumulator.AttackCount = checked(accumulator.AttackCount + 1);
                accumulator.LastAttackAt = Later(accumulator.LastAttackAt, attack.LastAttackAt);
            }

            poolAttackers.Clear();
        }

        await foreach (var row in attackGroups.AsAsyncEnumerable().WithCancellation(ct))
        {
            var attack = new AwdAttackTuple(
                row.CompetitionChallengeId,
                row.AttackerId,
                row.VictimId,
                row.RoundId,
                row.LastAttackAt);
            if (currentChallengeId != attack.CompetitionChallengeId
                || currentVictimId != attack.VictimId
                || currentRoundId != attack.RoundId)
            {
                ApplyAttackPool();
                currentChallengeId = attack.CompetitionChallengeId;
                currentVictimId = attack.VictimId;
                currentRoundId = attack.RoundId;
            }
            poolAttackers.Add(attack);
        }
        ApplyAttackPool();

        var roundStates = db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.CompetitionChallengeId != null
                && activeChallengeIds.Contains(flag.CompetitionChallengeId.Value)
                && flag.TeamId != null
                && activeTeamIds.Contains(flag.TeamId.Value)
                && flag.SpecificationKind == SpecificationKind.AwdRound
                && flag.SpecificationId != null
                && flag.ValidStart != null
                && flag.ValidUntil != null
                && flag.ValidUntil <= projectedAt
                && (flag.CompetitionChallengeId == overrideId && (overrideOpening == null || flag.ValidStart >= overrideOpening)
                    && (overrideScoringEnd == null || flag.ValidUntil <= overrideScoringEnd)
                    || db.CompetitionChallenges.Any(challenge => challenge.Id == flag.CompetitionChallengeId && challenge.Id != overrideId
                    && (challenge.AutoOpenAt == null || flag.ValidStart >= challenge.AutoOpenAt)
                    && (challenge.ScoringEndsAt == null || flag.ValidUntil <= challenge.ScoringEndsAt))))
            .Select(flag => new
            {
                CompetitionChallengeId = flag.CompetitionChallengeId!.Value,
                TeamId = flag.TeamId!.Value,
                Result = db.GameplayFacts.AsNoTracking()
                    .Where(fact => fact.CompetitionId == competitionId
                        && fact.TeamId == flag.TeamId
                        && fact.CompetitionChallengeId == flag.CompetitionChallengeId
                        && fact.Kind == GameplayFactKind.AwdServiceTransition
                        && fact.State == GameplayFactState.Completed
                        && (fact.Result == GameplayFactResult.ServiceUp
                            || fact.Result == GameplayFactResult.ServiceDown)
                        && fact.OccurredAt < flag.ValidUntil)
                    .OrderByDescending(fact => fact.OccurredAt)
                    .ThenByDescending(fact => fact.Id)
                    .Select(fact => fact.Result)
                    .FirstOrDefault()
            });
        var availability = await roundStates
            .GroupBy(round => new
            {
                round.CompetitionChallengeId,
                round.TeamId,
                IsDown = round.Result == GameplayFactResult.ServiceDown
            })
            .Select(group => new AwdAvailabilityAggregate(
                group.Key.CompetitionChallengeId,
                group.Key.TeamId,
                group.Key.IsDown,
                group.Count()))
            .ToListAsync(ct);
        foreach (var item in availability)
        {
            if (!accumulators.TryGetValue((item.TeamId, item.CompetitionChallengeId), out var accumulator))
                continue;
            var settings = settingsByChallenge[item.CompetitionChallengeId];
            if (item.IsDown)
                accumulator.Score = checked(
                    accumulator.Score - checked((long)item.Count * settings.ServiceUnhealthyPenalty));
            else
            {
                accumulator.Score = checked(
                    accumulator.Score + checked((long)item.Count * settings.ServiceHealthyPoints));
                accumulator.UpRoundCount = checked(accumulator.UpRoundCount + item.Count);
            }
        }

        return accumulators.Select(pair => new LeaderboardAwdAggregateFact(
                pair.Key.TeamId,
                pair.Key.ChallengeId,
                pair.Value.Score,
                pair.Value.AttackPoints,
                pair.Value.AttackCount,
                pair.Value.UpRoundCount,
                pair.Value.LastAttackAt,
                checked(pair.Value.AttackPoints + (long)pair.Value.UpRoundCount * settingsByChallenge[pair.Key.ChallengeId].ServiceHealthyPoints)))
            .ToArray();
    }

    private static AwdScoringSettings Effective(
        AwdConfiguration competition,
        CompetitionChallengeRules? challengeRules)
    {
        var challenge = challengeRules is AwdCompetitionChallengeRules awd
            ? TypedGameModeConfiguration.Awd(awd)
            : null;
        return new(
            challenge?.AttackRewardMode ?? competition.AttackRewardMode,
            challenge?.AttackPoints ?? competition.AttackPoints,
            challenge?.VictimDefensePoolPoints ?? competition.VictimDefensePoolPoints,
            challenge?.ServiceHealthyPoints ?? competition.ServiceHealthyPoints,
            challenge?.ServiceUnhealthyPenalty ?? competition.ServiceUnhealthyPenalty);
    }

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset right) =>
        left is null || right > left ? right : left;

    private static async Task<List<FactSummary>> ReadAwdWindowAsync(
        IQueryable<GameplayFact> query,
        IReadOnlyList<LeaderboardAwdRoundFact> rounds,
        CancellationToken ct)
    {
        if (rounds.Count == 0)
            return [];
        var roundIds = rounds.Select(round => round.RoundId).Distinct().ToArray();
        var startsAt = rounds.Min(round => round.StartsAt);
        var endsAt = rounds.Max(round => round.EndsAt);
        var rows = await query
            .Where(fact => fact.Kind == GameplayFactKind.FlagAttempt
                && fact.ReferenceKind == GameplayFactReferenceKind.AwdRound
                && fact.ReferenceId != null
                && roundIds.Contains(fact.ReferenceId.Value))
            .GroupBy(fact => new
            {
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ReferenceKind,
                fact.ReferenceId,
                fact.VictimTeamId,
                fact.ActorUserId
            })
            .Select(group => new FactSummary(
                group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id).Select(fact => fact.Id).First(),
                group.Key.TeamId,
                group.Key.CompetitionChallengeId,
                group.Key.Kind,
                group.Min(fact => fact.OccurredAt),
                group.Key.State,
                group.Key.Result,
                group.Key.FailureCode,
                group.Key.ReferenceKind,
                group.Key.ReferenceId,
                group.Key.VictimTeamId,
                group.Key.ActorUserId,
                null,
                group.Count(),
                group.Max(fact => fact.OccurredAt)))
            .ToListAsync(ct);
        var inWindow = query.Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition
            && fact.State == GameplayFactState.Completed
            && (fact.Result == GameplayFactResult.ServiceUp
                || fact.Result == GameplayFactResult.ServiceDown)
            && fact.OccurredAt >= startsAt
            && fact.OccurredAt < endsAt);
        var carryIds = query
            .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition
                && fact.State == GameplayFactState.Completed
                && (fact.Result == GameplayFactResult.ServiceUp
                    || fact.Result == GameplayFactResult.ServiceDown)
                && fact.OccurredAt < startsAt)
            .GroupBy(fact => new { fact.TeamId, fact.CompetitionChallengeId })
            .Select(group => group.OrderByDescending(fact => fact.OccurredAt)
                .ThenByDescending(fact => fact.Id)
                .Select(fact => fact.Id)
                .First());
        rows.AddRange(await inWindow.Concat(query.Where(fact => carryIds.Contains(fact.Id)))
            .Select(fact => new FactSummary(
                fact.Id,
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.OccurredAt,
                fact.State,
                fact.Result,
                fact.FailureCode,
                null,
                null,
                null,
                fact.ActorUserId,
                null,
                1,
                fact.OccurredAt))
            .ToListAsync(ct));
        return rows;
    }

    private static Task<List<FactSummary>> ReadAwdpWindowAsync(
        IQueryable<GameplayFact> query,
        Expression<Func<GameplayFact, AwdpGroupingKey>> keySelector,
        CancellationToken ct) => query
        .Where(fact => fact.Kind == GameplayFactKind.BreakAttempt
            || fact.Kind == GameplayFactKind.FixAttempt)
        .GroupBy(keySelector)
        .Select(group => new FactSummary(
            group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id).Select(fact => fact.Id).First(),
            group.Key.TeamId,
            group.Key.CompetitionChallengeId,
            group.Key.Kind,
            group.Min(fact => fact.OccurredAt),
            group.Key.State,
            group.Key.Result,
            group.Key.FailureCode,
            null,
            null,
            group.Key.VictimTeamId,
            group.Key.ActorUserId,
            group.Key.Value,
            group.Count(),
            group.Max(fact => fact.OccurredAt)))
        .ToListAsync(ct);

    private static Task<List<FactSummary>> ReadAwdpAggregateAsync(
        IQueryable<GameplayFact> query,
        DateTimeOffset settledPenaltyCutoff,
        bool includePenaltyCutoff,
        CancellationToken ct) => query
        .Where(fact => fact.Kind == GameplayFactKind.ManualAdjustment
            || ((fact.Kind == GameplayFactKind.BreakAttempt
                    || fact.Kind == GameplayFactKind.FixAttempt)
                && (fact.Result == GameplayFactResult.Correct
                    || (includePenaltyCutoff
                        ? fact.OccurredAt <= settledPenaltyCutoff
                        : fact.OccurredAt < settledPenaltyCutoff))))
        .GroupBy(fact => new
        {
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.State,
            fact.Result,
            fact.FailureCode,
            fact.VictimTeamId,
            fact.ActorUserId,
            Value = fact.Kind == GameplayFactKind.ManualAdjustment ? fact.Value : null
        })
        .Select(group => new FactSummary(
            group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id).Select(fact => fact.Id).First(),
            group.Key.TeamId,
            group.Key.CompetitionChallengeId,
            group.Key.Kind,
            group.Min(fact => fact.OccurredAt),
            group.Key.State,
            group.Key.Result,
            group.Key.FailureCode,
            null,
            null,
            group.Key.VictimTeamId,
            group.Key.ActorUserId,
            group.Key.Value,
            group.Count(),
            group.Max(fact => fact.OccurredAt)))
        .ToListAsync(ct);

    private static async Task<List<FactSummary>> ReadKohAsync(
        IQueryable<GameplayFact> query,
        IReadOnlyList<LeaderboardTeamFact> teams,
        CancellationToken ct)
    {
        var activeTeamIds = teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .Select(team => team.Id)
            .ToArray();
        var competitiveTeamIds = teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToArray();
        var scoringTeamIds = teams
            .Where(team => !team.IsBanned && !team.IsDeleted
                && team.EarnsScore
                && team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToArray();

        // KoH scoring is a state machine: an uncontrolled observation clears the current king,
        // while a controlled observation from a competitive team replaces it. Resolve that state
        // inside PostgreSQL before grouping so projection memory stays bounded without changing the
        // ordering semantics used by KohLeaderboardProjection.
        var observations = await query
            .Where(fact => fact.Kind == GameplayFactKind.KohControlObservation
                && fact.Result == GameplayFactResult.Controlled
                && fact.TeamId != null
                && activeTeamIds.Contains(fact.TeamId.Value))
            .Select(fact => new
            {
                Fact = fact,
                EffectiveTeamId = query
                    .Where(candidate => candidate.Kind == GameplayFactKind.KohControlObservation
                        && candidate.CompetitionChallengeId == fact.CompetitionChallengeId
                        && (candidate.OccurredAt < fact.OccurredAt
                            || (candidate.OccurredAt == fact.OccurredAt
                                && candidate.Id.CompareTo(fact.Id) <= 0))
                        && (candidate.Result == GameplayFactResult.Uncontrolled
                            || (candidate.Result == GameplayFactResult.Controlled
                                && candidate.TeamId != null
                                && competitiveTeamIds.Contains(candidate.TeamId.Value))))
                    .OrderByDescending(candidate => candidate.OccurredAt)
                    .ThenByDescending(candidate => candidate.Id)
                    .Select(candidate => candidate.Result == GameplayFactResult.Uncontrolled
                        ? (Guid?)null
                        : candidate.TeamId)
                    .FirstOrDefault()
            })
            .Where(row => row.EffectiveTeamId != null
                && scoringTeamIds.Contains(row.EffectiveTeamId.Value))
            .GroupBy(row => new
            {
                TeamId = row.EffectiveTeamId,
                row.Fact.CompetitionChallengeId,
                row.Fact.State,
                row.Fact.Result,
                row.Fact.FailureCode,
                row.Fact.ActorUserId
            })
            .Select(group => new FactSummary(
                group.OrderBy(row => row.Fact.OccurredAt)
                    .ThenBy(row => row.Fact.Id)
                    .Select(row => row.Fact.Id)
                    .First(),
                group.Key.TeamId,
                group.Key.CompetitionChallengeId,
                GameplayFactKind.KohControlObservation,
                group.Min(row => row.Fact.OccurredAt),
                group.Key.State,
                group.Key.Result,
                group.Key.FailureCode,
                null,
                null,
                null,
                group.Key.ActorUserId,
                null,
                group.Count(),
                group.Max(row => row.Fact.OccurredAt)))
            .ToListAsync(ct);

        observations.AddRange(await query
            .Where(fact => fact.Kind == GameplayFactKind.ManualAdjustment)
            .GroupBy(fact => new
            {
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ActorUserId,
                Value = fact.Kind == GameplayFactKind.ManualAdjustment ? fact.Value : null
            })
            .Select(group => new FactSummary(
                group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id).Select(fact => fact.Id).First(),
                group.Key.TeamId,
                group.Key.CompetitionChallengeId,
                group.Key.Kind,
                group.Min(fact => fact.OccurredAt),
                group.Key.State,
                group.Key.Result,
                group.Key.FailureCode,
                null,
                null,
                null,
                group.Key.ActorUserId,
                group.Key.Value,
                group.Count(),
                group.Max(fact => fact.OccurredAt)))
            .ToListAsync(ct));
        return observations;
    }

    private static Expression<Func<GameplayFact, AwdpGroupingKey>> BuildRoundSelector(
        DateTimeOffset? competitionStart,
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        AwdpProjectionWindow window)
    {
        var boundaries = Enumerable.Range(window.StartRound, window.EndRound - window.StartRound + 1)
            .Select(number => (Number: number, End: EffectiveClockToWallTime(
                lifecycle,
                competitionStart,
                TimeSpan.FromSeconds((long)number * window.DurationSeconds))))
            .ToArray();

        var fact = Expression.Parameter(typeof(GameplayFact), "fact");
        var occurredAt = Expression.Property(fact, nameof(GameplayFact.OccurredAt));
        Expression round = Expression.Constant(window.EndRound);
        foreach (var boundary in boundaries.Reverse())
        {
            round = Expression.Condition(
                Expression.LessThan(occurredAt, Expression.Constant(boundary.End)),
                Expression.Constant(boundary.Number),
                round);
        }
        var constructor = typeof(AwdpGroupingKey).GetConstructors().Single();
        var body = Expression.New(constructor,
            Expression.Property(fact, nameof(GameplayFact.TeamId)),
            Expression.Convert(
                Expression.Property(fact, nameof(GameplayFact.CompetitionChallengeId)),
                typeof(Guid?)),
            Expression.Property(fact, nameof(GameplayFact.Kind)),
            Expression.Property(fact, nameof(GameplayFact.State)),
            Expression.Property(fact, nameof(GameplayFact.Result)),
            Expression.Property(fact, nameof(GameplayFact.FailureCode)),
            Expression.Property(fact, nameof(GameplayFact.VictimTeamId)),
            Expression.Property(fact, nameof(GameplayFact.ActorUserId)),
            Expression.Condition(
                Expression.Equal(
                    Expression.Property(fact, nameof(GameplayFact.Kind)),
                    Expression.Constant(GameplayFactKind.ManualAdjustment)),
                Expression.Property(fact, nameof(GameplayFact.Value)),
                Expression.Constant(null, typeof(string))),
            round);
        return Expression.Lambda<Func<GameplayFact, AwdpGroupingKey>>(body, fact);
    }

    private static AwdpProjectionWindow BuildAwdpWindow(
        CompetitionModeConfiguration? configuration,
        DateTimeOffset? competitionStart,
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset projectedAt,
        CompetitionStatus competitionStatus,
        int? endingRound)
    {
        var duration = ReadRoundDuration(configuration);
        var elapsed = EffectiveElapsed(lifecycle, competitionStart, projectedAt);
        var elapsedSeconds = Math.Max(0, elapsed.TotalSeconds);
        var completedRounds = checked((int)(elapsedSeconds / duration));
        var competitionFinished = competitionStatus == CompetitionStatus.Finished;
        var latestRound = Math.Max(1, competitionFinished
            ? checked((int)Math.Ceiling(elapsedSeconds / duration))
            : checked(completedRounds + 1));
        var endRound = Math.Clamp(endingRound ?? latestRound, 1, latestRound);
        var startRound = Math.Max(1, endRound - ScoreboardRoundWindow.DefaultSize + 1);
        var settledPenaltyCutoff = competitionFinished
            ? lifecycle.LastOrDefault(item => item.To == CompetitionStatus.Finished)?.OccurredAt
                ?? projectedAt
            : EffectiveClockToWallTime(
                lifecycle,
                competitionStart,
                TimeSpan.FromSeconds((long)completedRounds * duration));
        return new(
            startRound,
            endRound,
            duration,
            EffectiveClockToWallTime(
                lifecycle,
                competitionStart,
                TimeSpan.FromSeconds((long)(startRound - 1) * duration)),
            EffectiveClockToWallTime(
                lifecycle,
                competitionStart,
                TimeSpan.FromSeconds((long)endRound * duration)),
            settledPenaltyCutoff,
            IncludePenaltyCutoff: competitionFinished);
    }

    private static int ReadRoundDuration(CompetitionModeConfiguration? configuration) =>
        configuration is AwdpCompetitionModeConfiguration { RoundDurationSeconds: > 0 } awdp
            ? awdp.RoundDurationSeconds
            : 300;

    private static TimeSpan EffectiveElapsed(
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset? competitionStart,
        DateTimeOffset at)
    {
        if (lifecycle.Count == 0)
            return at - competitionStart.GetValueOrDefault(at);
        var accumulated = TimeSpan.Zero;
        DateTimeOffset? runningSince = null;
        foreach (var transition in lifecycle
                     .Where(item => item.OccurredAt <= at)
                     .OrderBy(item => item.OccurredAt)
                     .ThenBy(item => item.Id))
        {
            if (transition.To == CompetitionStatus.Running && runningSince is null)
                runningSince = transition.OccurredAt;
            else if (transition.From == CompetitionStatus.Running
                     && transition.To != CompetitionStatus.Running
                     && runningSince is DateTimeOffset started)
            {
                accumulated += transition.OccurredAt - started;
                runningSince = null;
            }
        }
        return runningSince is DateTimeOffset current ? accumulated + (at - current) : accumulated;
    }

    private static DateTimeOffset EffectiveClockToWallTime(
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset? competitionStart,
        TimeSpan target) => AwdEffectiveRunningClock.ToWallTime(
            lifecycle, competitionStart.GetValueOrDefault(), target);

    private sealed record AwdpGroupingKey(
        Guid? TeamId,
        Guid? CompetitionChallengeId,
        GameplayFactKind Kind,
        GameplayFactState State,
        GameplayFactResult? Result,
        GameplayFactFailureCode? FailureCode,
        Guid? VictimTeamId,
        Guid? ActorUserId,
        string? Value,
        int Round);

    private sealed record AwdpProjectionWindow(
        int StartRound,
        int EndRound,
        int DurationSeconds,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        DateTimeOffset SettledPenaltyCutoff,
        bool IncludePenaltyCutoff);

    private sealed record AwdAttackTuple(
        Guid CompetitionChallengeId,
        Guid AttackerId,
        Guid VictimId,
        Guid RoundId,
        DateTimeOffset LastAttackAt);

    private sealed record AwdAvailabilityAggregate(
        Guid CompetitionChallengeId,
        Guid TeamId,
        bool IsDown,
        int Count);

    private sealed record AwdScoringSettings(
        AttackRewardMode AttackRewardMode,
        long AttackPoints,
        long VictimDefensePoolPoints,
        long ServiceHealthyPoints,
        long ServiceUnhealthyPenalty);

    private sealed class AwdAggregateAccumulator
    {
        public long Score { get; set; }
        public long AttackPoints { get; set; }
        public int AttackCount { get; set; }
        public int UpRoundCount { get; set; }
        public DateTimeOffset? LastAttackAt { get; set; }
    }

    private sealed record FactSummary(
        Guid Id,
        Guid? TeamId,
        Guid? CompetitionChallengeId,
        GameplayFactKind Kind,
        DateTimeOffset OccurredAt,
        GameplayFactState State,
        GameplayFactResult? Result,
        GameplayFactFailureCode? FailureCode,
        GameplayFactReferenceKind? ReferenceKind,
        Guid? ReferenceId,
        Guid? VictimTeamId,
        Guid? ActorUserId,
        string? Value,
        int Multiplicity,
        DateTimeOffset LastOccurredAt);
}

internal sealed record LeaderboardFactProjectionRows(
    IReadOnlyList<LeaderboardGameplayFact> Aggregate,
    IReadOnlyList<LeaderboardGameplayFact> Scoreboard,
    IReadOnlyList<LeaderboardAwdAggregateFact>? AwdAggregates = null);
