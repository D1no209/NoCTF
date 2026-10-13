using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;
using NoCTF.GameModes.Awd.Scheduling;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>
/// Produces relational scoring summaries and a bounded display window. AWD uses issuance identities
/// and structured check evidence; raw submissions remain available through the signed cursor reader.
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
        NoCTF.Application.Challenges.Timing.ChallengeTimingProjectionOverride? timingOverride = null,
        NoCTF.Application.Competitions.Awd.AwdScoringProjectionOverride? awdScoringOverride = null)
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
            var roundScores = await AwdScoreProjectionReader.ReadAsync(db, competitionId, challenges, teams, projectedAt, ct, awdScoringOverride);
            var aggregates = AwdScoreProjectionReader.Aggregate(roundScores);
            var aggregateRows = await ReadAwdManualAdjustmentsAsync(query, ct);
            var windowRows = await ReadAwdWindowAsync(displayQuery, awdWindowRounds ?? [], ct);
            if (awdWindowRounds is { Count: > 0 })
            {
                var carry = AwdScoreProjectionReader.Aggregate(roundScores
                    .Where(score => score.RoundNumber < awdWindowRounds.Min(x => x.Number)).ToArray());
                var prior = carry.ToDictionary(x => (x.TeamId, x.CompetitionChallengeId), x => x.PositivePoints);
                aggregates = aggregates.Select(x => x with { PositivePointsBeforeWindow = prior.GetValueOrDefault((x.TeamId, x.CompetitionChallengeId)) }).ToArray();
            }
            return new(
                Map(aggregateRows, hintCosts),
                Map(windowRows.Concat(aggregateRows), hintCosts),
                aggregates, roundScores);
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
                row.CompetitionChallengeId!.Value,
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

    private static Task<List<FactSummary>> ReadAwdWindowAsync(
        IQueryable<GameplayFact> query, IReadOnlyList<LeaderboardAwdRoundFact> rounds, CancellationToken ct)
    {
        var roundIds = rounds.Select(round => round.RoundId).Distinct().ToArray();
        return query.Where(fact => fact.Kind == GameplayFactKind.FlagAttempt && fact.Awd != null
                && fact.Awd.ReceivedRoundId != null && roundIds.Contains(fact.Awd.ReceivedRoundId.Value))
            .Select(fact => new FactSummary(fact.Id, fact.TeamId, fact.CompetitionChallengeId, fact.Kind,
                fact.OccurredAt, fact.State, fact.Result, fact.FailureCode, GameplayFactReferenceKind.AwdRound,
                fact.Awd!.ReceivedRoundId, fact.VictimTeamId, fact.ActorUserId, null, 1, fact.OccurredAt))
            .ToListAsync(ct);
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
    IReadOnlyList<LeaderboardAwdAggregateFact>? AwdAggregates = null,
    IReadOnlyList<AwdRoundScoreProjection>? AwdRoundScores = null);
