using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>
/// Produces the bounded fact summaries used by the stable leaderboard snapshot. Raw immutable facts
/// remain available through the signed-cursor detail reader; the maintenance projection never loads
/// the complete competition history.
/// </summary>
internal static class LeaderboardFactProjectionReader
{
    public static async Task<IReadOnlyList<LeaderboardGameplayFact>> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        GameMode mode,
        string? competitionConfigurationJson,
        DateTimeOffset? competitionStart,
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset projectedAt,
        IReadOnlyDictionary<Guid, long> hintCosts,
        IReadOnlyList<LeaderboardTeamFact> teams,
        CancellationToken ct)
    {
        var query = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId && fact.OccurredAt <= projectedAt);
        var rows = mode switch
        {
            GameMode.Ctf => await ReadCtfAsync(query, ct),
            GameMode.Awd => await ReadAwdAsync(db, competitionId, query, ct),
            GameMode.Awdp => await ReadAwdpAsync(
                query,
                BuildRoundSelector(
                    competitionConfigurationJson,
                    competitionStart,
                    lifecycle,
                    projectedAt),
                ct),
            GameMode.Koh => await ReadKohAsync(query, teams, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        return rows.Select(row => new LeaderboardGameplayFact(
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
    }

    private static Task<List<FactSummary>> ReadCtfAsync(
        IQueryable<GameplayFact> query,
        CancellationToken ct) => query
        .Where(fact => fact.Kind == GameplayFactKind.FlagAttempt
            || fact.Kind == GameplayFactKind.HintUnlock
            || fact.Kind == GameplayFactKind.ManualAdjustment)
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

    private static async Task<List<FactSummary>> ReadAwdAsync(
        NoCtfDbContext db,
        Guid competitionId,
        IQueryable<GameplayFact> query,
        CancellationToken ct)
    {
        var rows = await query
            .Where(fact => fact.Kind == GameplayFactKind.FlagAttempt
                || fact.Kind == GameplayFactKind.ManualAdjustment)
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

        var latestTransitionIds = db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.TeamId != null
                && flag.CompetitionChallengeId != null
                && flag.SpecificationKind == SpecificationKind.AwdRound
                && flag.ValidUntil != null
                && db.CompetitionChallenges.Any(challenge => challenge.Id == flag.CompetitionChallengeId
                    && challenge.CompetitionId == competitionId))
            .Select(flag => db.GameplayFacts
                .Where(fact => fact.CompetitionId == competitionId
                    && fact.TeamId == flag.TeamId
                    && fact.CompetitionChallengeId == flag.CompetitionChallengeId
                    && fact.Kind == GameplayFactKind.AwdServiceTransition
                    && fact.OccurredAt < flag.ValidUntil)
                .OrderByDescending(fact => fact.OccurredAt)
                .ThenByDescending(fact => fact.Id)
                .Select(fact => (Guid?)fact.Id)
                .FirstOrDefault())
            .Where(id => id != null)
            .Distinct();
        rows.AddRange(await db.GameplayFacts.AsNoTracking()
            .Where(fact => latestTransitionIds.Contains(fact.Id))
            .Select(fact => new FactSummary(
                fact.Id,
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.OccurredAt,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ReferenceKind,
                fact.ReferenceId,
                fact.VictimTeamId,
                fact.ActorUserId,
                null,
                1,
                fact.OccurredAt))
            .ToListAsync(ct));
        return rows;
    }

    private static Task<List<FactSummary>> ReadAwdpAsync(
        IQueryable<GameplayFact> query,
        Expression<Func<GameplayFact, AwdpGroupingKey>> keySelector,
        CancellationToken ct) => query
        .Where(fact => fact.Kind == GameplayFactKind.BreakAttempt
            || fact.Kind == GameplayFactKind.FixAttempt
            || fact.Kind == GameplayFactKind.ManualAdjustment)
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
        string? configurationJson,
        DateTimeOffset? competitionStart,
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset projectedAt)
    {
        var duration = ReadRoundDuration(configurationJson);
        var elapsed = EffectiveElapsed(lifecycle, competitionStart, projectedAt);
        var roundCount = Math.Max(1, checked((int)(Math.Max(0, elapsed.TotalSeconds) / duration) + 1));
        var boundaries = Enumerable.Range(1, roundCount)
            .Select(number => (Number: number, End: EffectiveClockToWallTime(
                lifecycle,
                competitionStart,
                TimeSpan.FromSeconds((long)number * duration))))
            .ToArray();

        var fact = Expression.Parameter(typeof(GameplayFact), "fact");
        var occurredAt = Expression.Property(fact, nameof(GameplayFact.OccurredAt));
        Expression round = Expression.Constant(roundCount);
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

    private static int ReadRoundDuration(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 300;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("roundDurationSeconds", out var value)
                && value.TryGetInt32(out var duration)
                && duration > 0
                    ? duration
                    : 300;
        }
        catch (JsonException)
        {
            return 300;
        }
    }

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
        TimeSpan target)
    {
        var transitions = lifecycle.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id).ToArray();
        if (transitions.Length == 0)
            return competitionStart.GetValueOrDefault() + target;
        var accumulated = TimeSpan.Zero;
        DateTimeOffset? runningSince = null;
        foreach (var transition in transitions)
        {
            if (transition.To == CompetitionStatus.Running && runningSince is null)
            {
                runningSince = transition.OccurredAt;
                continue;
            }
            if (transition.From != CompetitionStatus.Running
                || transition.To == CompetitionStatus.Running
                || runningSince is not DateTimeOffset segmentStart)
                continue;
            var segment = transition.OccurredAt - segmentStart;
            if (accumulated + segment >= target)
                return segmentStart + (target - accumulated);
            accumulated += segment;
            runningSince = null;
        }
        return (runningSince ?? competitionStart.GetValueOrDefault()) + (target - accumulated);
    }

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
