using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using System.Text.Json;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Ctf.Scoring;
using System.Globalization;

namespace NoCTF.GameModes.Leaderboard;

public sealed class CtfLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Ctf;

    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input) =>
        CtfLeaderboardProjection.Project(input);
}

internal static class CtfLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CtfScoreExpression ScoreExpression = new();
    private const string DefaultScoreExpression =
        "solveCount <= 1 ? initialPoints : solveCount >= decayParameter ? minimumPoints : initialPoints + (minimumPoints - initialPoints) * ((solveCount - 1m) / (decayParameter - 1m)) * ((solveCount - 1m) / (decayParameter - 1m))";

    public static GameModeLeaderboardProjection Project(LeaderboardProjectionInput input)
    {
        var (entries, series) = ProjectCore(input);
        return new(entries, series);
    }

    private static (IReadOnlyList<LeaderboardEntry> Entries, IReadOnlyList<LeaderboardTeamSeries> Series)
        ProjectCore(LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted).ToDictionary(challenge => challenge.Id);
        var defaults = ParseCompetition(input.CompetitionConfigurationJson);
        var solves = input.Submissions
            .Where(fact => validTeams.ContainsKey(fact.TeamId)
                          && fact.Kind == SubmissionKind.Flag
                          && fact.Event is { DeletedAt: null, Result: ScoringResult.Correct }
                          && fact.CompetitionChallengeId is not null
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .GroupBy(fact => (fact.TeamId, fact.CompetitionChallengeId))
            .Select(group => group.First())
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();
        var currentSolveCounts = solves
            .GroupBy(solve => solve.CompetitionChallengeId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var awarded = new Dictionary<Guid, List<(LeaderboardSubmissionFact Fact, long Points, int SolveOrdinal)>>();
        var solveNumber = new Dictionary<Guid, int>();
        foreach (var solve in solves)
        {
            var challengeId = solve.CompetitionChallengeId!.Value;
            var configuration = ParseChallenge(challenges.TryGetValue(challengeId, out var challenge)
                ? challenge.ConfigurationJson
                : null);
            var points = configuration.Points ?? defaults.DefaultPoints;
            var index = solveNumber.GetValueOrDefault(challengeId);
            var solveOrdinal = checked(index + 1);
            var expression = configuration.ScoreExpression
                ?? defaults.ScoreExpression
                ?? DefaultScoreExpression;
            var score = ScoreExpression.Evaluate(expression, new(
                points.InitialPoints,
                points.MinimumPoints,
                currentSolveCounts[challengeId],
                validTeams.Count,
                points.DecayFactor));
            score = checked(score + BloodRewardAt(
                configuration.BloodRewards ?? defaults.BloodRewards,
                index,
                score,
                () => currentSolveCounts[challengeId] == solveOrdinal
                    ? score
                    : ScoreExpression.Evaluate(expression, new(
                        points.InitialPoints,
                        points.MinimumPoints,
                        solveOrdinal,
                        validTeams.Count,
                        points.DecayFactor)),
                points));
            solveNumber[challengeId] = index + 1;
            if (!awarded.TryGetValue(solve.TeamId, out var teamSolves))
                awarded[solve.TeamId] = teamSolves = [];
            teamSolves.Add((solve, score, solveOrdinal));
        }

        var wrongFacts = input.Submissions
            .Where(fact => validTeams.ContainsKey(fact.TeamId)
                && fact.Kind == SubmissionKind.Flag
                && fact.Event is { DeletedAt: null, Result: ScoringResult.Wrong })
            .Select(fact =>
                (fact.TeamId,
                fact.ReceivedAt,
                fact.SubmissionId,
                Penalty: (fact.CompetitionChallengeId is Guid challengeId
                        ? ParseChallenge(challenges.GetValueOrDefault(challengeId)?.ConfigurationJson)
                        : null)
                    ?.WrongSubmissionPenalty ?? defaults.WrongSubmissionPenalty))
            .ToList();
        var wrongPenalties = wrongFacts
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + fact.Penalty)));
        var hintCosts = ProjectionPenalties.HintCosts(input, validTeams.Keys);
        var manualAdjustments = ProjectionPenalties.ManualAdjustments(input, validTeams.Keys);

        var rows = validTeams.Values.Select(team =>
        {
            var own = awarded.GetValueOrDefault(team.Id) ?? [];
            var summaries = own.GroupBy(item => item.Fact.CompetitionChallengeId!.Value)
                .Select(group => new LeaderboardChallengeSummary(
                    group.Key,
                    challenges.TryGetValue(group.Key, out var challenge) ? challenge.Direction : string.Empty,
                    group.Count()))
                .OrderBy(summary => summary.CompetitionChallengeId)
                .ToList();
            var last = own.Select(item => item.Fact.ReceivedAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var total = checked(own.Aggregate(0L, (sum, item) => checked(sum + item.Points))
                - wrongPenalties.GetValueOrDefault(team.Id)
                - hintCosts.GetValueOrDefault(team.Id)
                + manualAdjustments.GetValueOrDefault(team.Id));
            return new CtfRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    total,
                    own.Count,
                    last == default ? null : last,
                    summaries),
                team.RegisteredAt);
        });
        var entries = rows
            .OrderByDescending(row => row.Entry.Score)
            .ThenBy(row => row.Entry.LastScoreAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(row => row.Entry.SolveCount)
            .ThenBy(row => row.RegisteredAt)
            .ThenBy(row => row.Entry.TeamId)
            .Select((row, index) => row.Entry with { Rank = index + 1 })
            .ToList();
        var series = BuildSeries(
            validTeams,
            awarded,
            wrongFacts,
            ProjectionPenalties.HintCostEvents(input, validTeams.Keys),
            ProjectionPenalties.ManualAdjustmentEvents(input, validTeams.Keys));
        return (entries, series);
    }

    private static IReadOnlyList<LeaderboardTeamSeries> BuildSeries(
        IReadOnlyDictionary<Guid, LeaderboardTeamFact> validTeams,
        IReadOnlyDictionary<Guid, List<(LeaderboardSubmissionFact Fact, long Points, int SolveOrdinal)>> awarded,
        IReadOnlyList<(Guid TeamId, DateTimeOffset ReceivedAt, Guid SubmissionId, long Penalty)> wrongFacts,
        IReadOnlyList<(Guid TeamId, DateTimeOffset OccurredAt, Guid EventId, long Cost)> hintCostEvents,
        IReadOnlyList<(Guid TeamId, DateTimeOffset OccurredAt, Guid EventId, long Delta)> manualAdjustmentEvents)
    {
        var deltas = new Dictionary<Guid, List<(DateTimeOffset At, Guid StableId, long Delta)>>();
        var solveRecords = new Dictionary<Guid, List<LeaderboardSolveRecord>>();
        var penaltyRecords = new Dictionary<Guid, List<LeaderboardPenaltyRecord>>();
        void Add(Guid teamId, DateTimeOffset at, Guid stableId, long delta)
        {
            if (!deltas.TryGetValue(teamId, out var events))
                deltas[teamId] = events = [];
            events.Add((at, stableId, delta));
        }
        void AddTo<T>(Dictionary<Guid, List<T>> records, Guid teamId, T record)
        {
            if (!records.TryGetValue(teamId, out var list))
                records[teamId] = list = [];
            list.Add(record);
        }
        foreach (var (teamId, solves) in awarded)
        foreach (var (fact, points, solveOrdinal) in solves)
        {
            Add(teamId, fact.ReceivedAt, fact.SubmissionId, points);
            AddTo(solveRecords, teamId, new LeaderboardSolveRecord(
                fact.CompetitionChallengeId!.Value,
                fact.ReceivedAt,
                points,
                solveOrdinal,
                string.IsNullOrWhiteSpace(fact.SubmitterName) ? null : fact.SubmitterName));
        }
        foreach (var fact in wrongFacts)
        {
            Add(fact.TeamId, fact.ReceivedAt, fact.SubmissionId, -fact.Penalty);
            AddTo(penaltyRecords, fact.TeamId, new LeaderboardPenaltyRecord(
                fact.ReceivedAt,
                fact.Penalty,
                LeaderboardPenaltyKind.WrongSubmission));
        }
        foreach (var hint in hintCostEvents)
        {
            Add(hint.TeamId, hint.OccurredAt, hint.EventId, -hint.Cost);
            AddTo(penaltyRecords, hint.TeamId, new LeaderboardPenaltyRecord(
                hint.OccurredAt,
                hint.Cost,
                LeaderboardPenaltyKind.HintUnlock));
        }
        foreach (var adjustment in manualAdjustmentEvents)
        {
            Add(adjustment.TeamId, adjustment.OccurredAt, adjustment.EventId, adjustment.Delta);
        }
        return deltas
            .Select(pair =>
            {
                var score = 0L;
                var points = pair.Value
                    .OrderBy(item => item.At)
                    .ThenBy(item => item.StableId)
                    .Select(item => new LeaderboardScorePoint(item.At, score = checked(score + item.Delta)))
                    .ToList();
                return new LeaderboardTeamSeries(pair.Key, validTeams[pair.Key].Name, points)
                {
                    Solves = solveRecords.GetValueOrDefault(pair.Key) ?? [],
                    Penalties = (penaltyRecords.GetValueOrDefault(pair.Key) ?? [])
                        .OrderBy(record => record.At)
                        .ToList()
                };
            })
            .OrderBy(series => series.TeamId)
            .ToList();
    }

    private static long BloodRewardAt(
        IReadOnlyList<BloodReward> rewards,
        int solveIndex,
        long currentPoints,
        Func<long> solvePoints,
        CtfPointConfiguration points)
    {
        if ((uint)solveIndex >= (uint)rewards.Count)
            return 0;

        var reward = rewards[solveIndex];
        var basis = reward.Policy switch
        {
            BloodRewardPolicy.FixedPoints => reward.Value,
            BloodRewardPolicy.InitialPointsPercentage => points.InitialPoints * reward.Value / 100m,
            BloodRewardPolicy.SolveTimePointsPercentage => solvePoints() * reward.Value / 100m,
            BloodRewardPolicy.CurrentPointsPercentage => currentPoints * reward.Value / 100m,
            _ => 0m
        };
        return checked((long)Math.Round(basis, MidpointRounding.AwayFromZero));
    }

    private static CtfConfiguration ParseCompetition(string? json) =>
        TryParse<CtfConfiguration>(json) ?? new(CtfConfiguration.CurrentSchemaVersion, new(500, 100, 10), [], null);

    private static CtfChallengeConfiguration ParseChallenge(string? json) =>
        TryParse<CtfChallengeConfiguration>(json)
        ?? new(CtfChallengeConfiguration.CurrentSchemaVersion, null, null);

    private static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private sealed record CtfRankedEntry(
        LeaderboardEntry Entry,
        DateTimeOffset RegisteredAt);
}

public sealed class AwdLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Awd;

    // Score timeline series are only implemented for CTF; other modes return an empty list.
    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input) =>
        new(AwdLeaderboardProjection.Project(input), []);
}

internal static class AwdLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
    {
        var configuration = TryParse(input.CompetitionConfigurationJson)
            ?? AwdConfiguration.Default;
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var values = teams.Keys.ToDictionary(team => team, _ => 0L);
        var attackPoints = teams.Keys.ToDictionary(team => team, _ => 0L);
        var upRoundCounts = teams.Keys.ToDictionary(team => team, _ => 0);
        var solves = input.Submissions
            .Where(fact => teams.ContainsKey(fact.TeamId)
                          && fact.Kind == SubmissionKind.Flag
                          && fact.Event is
                          {
                              DeletedAt: null,
                              Result: ScoringResult.Correct,
                              SpecificationKind: NoCTF.Domain.Challenges.SpecificationKind.AwdRound,
                              SpecificationId: not null
                          }
                          && fact.CompetitionChallengeId is not null
                          && fact.VictimTeamId is not null
                          && fact.VictimTeamId != fact.TeamId
                          && teams.ContainsKey(fact.VictimTeamId.Value)
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();
        var attacks = solves
            .GroupBy(fact => new
            {
                ChallengeId = fact.CompetitionChallengeId!.Value,
                RoundId = fact.Event.SpecificationId!.Value,
                VictimId = fact.VictimTeamId!.Value,
                AttackerId = fact.TeamId
            })
            .Select(group => group.First())
            .ToList();
        foreach (var pool in attacks.GroupBy(fact => new
        {
            ChallengeId = fact.CompetitionChallengeId!.Value,
            RoundId = fact.Event.SpecificationId!.Value,
            VictimId = fact.VictimTeamId!.Value
        }))
        {
            var settings = Effective(configuration, challenges.GetValueOrDefault(pool.Key.ChallengeId)?.ConfigurationJson);
            var attackers = pool.Select(fact => fact.TeamId).Distinct().ToList();
            var reward = settings.AttackRewardMode == AttackRewardMode.FixedPerAttack
                ? settings.AttackPoints
                : settings.VictimDefensePoolPoints / attackers.Count;
            foreach (var attacker in attackers)
            {
                values[attacker] = checked(values[attacker] + reward);
                attackPoints[attacker] = checked(attackPoints[attacker] + reward);
            }
            values[pool.Key.VictimId] = checked(
                values[pool.Key.VictimId] - settings.VictimDefensePoolPoints);
        }
        var serviceStates = input.SystemEvents
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Kind: ScoringEventKind.AwdServiceStatus,
                TeamId: not null,
                CompetitionChallengeId: not null,
                Result: ScoringResult.Correct or ScoringResult.Wrong
            })
            .OrderBy(fact => fact.Event.OccurredAt)
            .ThenBy(fact => fact.Event.Id)
            .ToList();
        var projectedAt = input.ProjectedAt ?? DateTimeOffset.UtcNow;
        foreach (var round in (input.AwdRounds ?? [])
                     .Where(round => teams.ContainsKey(round.TeamId)
                         && challenges.ContainsKey(round.CompetitionChallengeId)
                         && round.StartsAt < round.EndsAt
                         && round.EndsAt <= projectedAt)
                     .GroupBy(round => new
                     {
                         round.TeamId,
                         round.CompetitionChallengeId,
                         round.RoundId
                     })
                     .Select(group => group.First()))
        {
            var latestState = serviceStates
                .Where(fact => fact.Event.TeamId == round.TeamId
                    && fact.Event.CompetitionChallengeId == round.CompetitionChallengeId
                    && fact.Event.OccurredAt < round.EndsAt)
                .LastOrDefault();
            var settings = Effective(
                configuration,
                challenges[round.CompetitionChallengeId].ConfigurationJson);
            if (latestState?.Event.Result == ScoringResult.Wrong)
            {
                values[round.TeamId] = checked(
                    values[round.TeamId] - settings.ServiceUnhealthyPenalty);
            }
            else
            {
                values[round.TeamId] = checked(
                    values[round.TeamId] + settings.ServiceHealthyPoints);
                upRoundCounts[round.TeamId] = checked(upRoundCounts[round.TeamId] + 1);
            }
        }
        foreach (var hint in ProjectionPenalties.HintCosts(input, teams.Keys))
            values[hint.Key] = checked(values[hint.Key] - hint.Value);
        foreach (var adjustment in ProjectionPenalties.ManualAdjustments(input, teams.Keys))
            values[adjustment.Key] = checked(values[adjustment.Key] + adjustment.Value);
        var rows = teams.Values.Select(team =>
        {
            var attackCount = attacks.Count(attack => attack.TeamId == team.Id);
            var lastAttackAt = LastAttackAt(attacks, team.Id);
            return new AwdRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    values[team.Id],
                    attackCount,
                    lastAttackAt,
                    []),
                attackPoints[team.Id],
                upRoundCounts[team.Id],
                attackCount,
                lastAttackAt,
                team.RegisteredAt);
        });
        return rows.OrderByDescending(row => row.Entry.Score)
            .ThenByDescending(row => row.AttackPoints)
            .ThenByDescending(row => row.UpRoundCount)
            .ThenByDescending(row => row.AttackCount)
            .ThenBy(row => row.LastAttackAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.RegisteredAt)
            .ThenBy(row => row.Entry.TeamId)
            .Select((row, index) => row.Entry with { Rank = index + 1 })
            .ToList();
    }

    private static DateTimeOffset? LastAttackAt(
        IReadOnlyList<LeaderboardSubmissionFact> attacks,
        Guid teamId)
    {
        var last = attacks.Where(attack => attack.TeamId == teamId)
            .Select(attack => attack.ReceivedAt)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        return last == default ? null : last;
    }

    private static AwdConfiguration? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AwdConfiguration>(json!, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static AwdScoringSettings Effective(AwdConfiguration competition, string? challengeJson)
    {
        var challenge = TryParseChallenge(challengeJson);
        return new(
            challenge?.AttackRewardMode ?? competition.AttackRewardMode,
            challenge?.AttackPoints ?? competition.AttackPoints,
            challenge?.VictimDefensePoolPoints ?? competition.VictimDefensePoolPoints,
            challenge?.ServiceHealthyPoints ?? competition.ServiceHealthyPoints,
            challenge?.ServiceUnhealthyPenalty ?? competition.ServiceUnhealthyPenalty);
    }

    private static AwdChallengeConfiguration? TryParseChallenge(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AwdChallengeConfiguration>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private sealed record AwdScoringSettings(
        AttackRewardMode AttackRewardMode,
        long AttackPoints,
        long VictimDefensePoolPoints,
        long ServiceHealthyPoints,
        long ServiceUnhealthyPenalty);

    private sealed record AwdRankedEntry(
        LeaderboardEntry Entry,
        long AttackPoints,
        int UpRoundCount,
        int AttackCount,
        DateTimeOffset? LastAttackAt,
        DateTimeOffset RegisteredAt);
}

public sealed class AwdpLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Awdp;

    // Score timeline series are only implemented for CTF; other modes return an empty list.
    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input) =>
        new(AwdpLeaderboardProjection.Project(input), []);
}

internal static class AwdpLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
    {
        var competition = ParseCompetition(input.CompetitionConfigurationJson);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted).ToDictionary(challenge => challenge.Id);
        var facts = input.Submissions
            .Where(fact => teams.ContainsKey(fact.TeamId)
                          && fact.Kind is SubmissionKind.Break or SubmissionKind.Fix
                          && fact.Event is { DeletedAt: null, Result: ScoringResult.Correct }
                          && fact.CompetitionChallengeId is not null
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();
        var correctBreaks = facts
            .Where(fact => fact.Kind == SubmissionKind.Break)
            .Select(fact => (fact.TeamId, fact.CompetitionChallengeId!.Value))
            .ToHashSet();
        var awarded = new Dictionary<Guid, List<(LeaderboardSubmissionFact Fact, long Points)>>();
        var milestones = new HashSet<(Guid TeamId, Guid ChallengeId, SubmissionKind Kind, int Round)>();
        foreach (var fact in facts)
        {
            var challengeId = fact.CompetitionChallengeId!.Value;
            var configuration = Effective(
                competition,
                challenges.GetValueOrDefault(challengeId)?.ConfigurationJson);
            if (fact.Kind == SubmissionKind.Fix
                && configuration.RequireBreakBeforeFix
                && !correctBreaks.Contains((fact.TeamId, challengeId)))
                continue;
            var achievement = fact.Kind == SubmissionKind.Break
                ? configuration.Break
                : configuration.Fix;
            var round = achievement.Settlement == AchievementSettlement.Milestone
                ? 0
                : Round(
                    fact.ReceivedAt,
                    input.LifecycleAudits,
                    input.CompetitionStartTime,
                    competition.RoundDurationSeconds);
            if (!milestones.Add((fact.TeamId, challengeId, fact.Kind, round)))
                continue;
            if (!awarded.TryGetValue(fact.TeamId, out var teamFacts))
                awarded[fact.TeamId] = teamFacts = [];
            teamFacts.Add((fact, achievement.Points));
        }
        var penalties = input.Submissions
            .Where(fact => teams.ContainsKey(fact.TeamId)
                           && fact.Kind is SubmissionKind.Break or SubmissionKind.Fix
                           && fact.Event.DeletedAt is null
                           && fact.CompetitionChallengeId is not null
                           && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total +
                    PenaltyFor(
                        fact,
                        Effective(
                            competition,
                            fact.CompetitionChallengeId is { } challengeId
                                ? challenges.GetValueOrDefault(challengeId)?.ConfigurationJson
                                : null)))));
        var hintCosts = ProjectionPenalties.HintCosts(input, teams.Keys);
        var manualAdjustments = ProjectionPenalties.ManualAdjustments(input, teams.Keys);
        var rows = teams.Values.Select(team =>
        {
            var own = awarded.GetValueOrDefault(team.Id) ?? [];
            var last = own.Select(item => item.Fact.ReceivedAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var lastFixAt = own
                .Where(item => item.Fact.Kind == SubmissionKind.Fix)
                .Select(item => (DateTimeOffset?)item.Fact.ReceivedAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var awardedScore = own.Aggregate(0L, (total, item) => checked(total + item.Points));
            var penalty = penalties.GetValueOrDefault(team.Id);
            return new AwdpRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(awardedScore - penalty - hintCosts.GetValueOrDefault(team.Id)
                        + manualAdjustments.GetValueOrDefault(team.Id)),
                    own.Count,
                    last == default ? null : last,
                    []),
                own.Count(item => item.Fact.Kind == SubmissionKind.Fix),
                own.Count(item => item.Fact.Kind == SubmissionKind.Break),
                penalty,
                lastFixAt,
                team.RegisteredAt);
        });
        return rows.OrderByDescending(row => row.Entry.Score)
            .ThenByDescending(row => row.FixCount)
            .ThenByDescending(row => row.BreakCount)
            .ThenBy(row => row.Penalty)
            .ThenBy(row => row.LastFixAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.RegisteredAt)
            .ThenBy(row => row.Entry.TeamId)
            .Select((row, index) => row.Entry with { Rank = index + 1 })
            .ToList();
    }

    private static int Round(
        DateTimeOffset occurredAt,
        IReadOnlyList<CompetitionLifecycleTransition>? lifecycleAudits,
        DateTimeOffset? start,
        int durationSeconds)
    {
        if (durationSeconds <= 0) return 1;
        var elapsed = lifecycleAudits is not null
            ? AwdEffectiveRunningClock.Calculate(lifecycleAudits, occurredAt)
            : start is { } startedAt
                ? occurredAt - startedAt
                : TimeSpan.Zero;
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        return checked((int)(seconds / durationSeconds) + 1);
    }

    private static AwdpConfiguration ParseCompetition(string? json) =>
        TryParse<AwdpConfiguration>(json)
        ?? new(AwdpConfiguration.CurrentSchemaVersion, 300, new(AchievementSettlement.PerRound, 50),
            new(AchievementSettlement.PerRound, 50), 100, 50);

    private static AwdpChallengeConfiguration ParseChallenge(string? json) =>
        TryParse<AwdpChallengeConfiguration>(json)
        ?? new(AwdpChallengeConfiguration.CurrentSchemaVersion, null, null, null, null, null);

    private static AwdpEffectiveConfiguration Effective(
        AwdpConfiguration competition,
        string? challengeJson) =>
        AwdpConfigurationResolver.Resolve(competition, ParseChallenge(challengeJson));

    private static long PenaltyFor(
        LeaderboardSubmissionFact fact,
        AwdpEffectiveConfiguration configuration) =>
        (fact.Kind, fact.Event.Result, fact.Event.FailureCode) switch
        {
            (SubmissionKind.Break, ScoringResult.Wrong, _)
                => configuration.BreakWrongPenalty,
            (SubmissionKind.Fix, ScoringResult.Wrong, ScoringFailureCode.AwdpFixFailed
                or ScoringFailureCode.AwdpPatchFailed or ScoringFailureCode.AwdpPatchTimeout)
                => configuration.FixFailurePenalty,
            (SubmissionKind.Fix, ScoringResult.Rejected, ScoringFailureCode.AwdpViolation)
                => configuration.ViolationPenalty,
            (SubmissionKind.Fix, ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown)
                => configuration.ServiceDownPenalty,
            _ => 0L
        };

    private sealed record AwdpRankedEntry(
        LeaderboardEntry Entry,
        int FixCount,
        int BreakCount,
        long Penalty,
        DateTimeOffset? LastFixAt,
        DateTimeOffset RegisteredAt);

    private static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}

public sealed class KohLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Koh;

    // Score timeline series are only implemented for CTF; other modes return an empty list.
    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input) =>
        new(KohLeaderboardProjection.Project(input), []);
}

internal static class KohLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
    {
        var configuration = ParseCompetition(input.CompetitionConfigurationJson);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var hasChallengeCatalog = input.Challenges is not null;
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var challengePoints = challenges.ToDictionary(
            item => item.Key,
            item => PointsFor(configuration, item.Value.ConfigurationJson));
        var observations = input.SystemEvents
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Kind: ScoringEventKind.KohObservation,
                Result: ScoringResult.Correct,
                TeamId: not null,
                CompetitionChallengeId: not null
            }
            && teams.ContainsKey(fact.Event.TeamId!.Value)
            && (!hasChallengeCatalog
                || challenges.ContainsKey(fact.Event.CompetitionChallengeId!.Value)))
            .OrderBy(fact => fact.Event.OccurredAt)
            .ThenBy(fact => fact.Event.Id)
            .ToList();
        var hintCosts = ProjectionPenalties.HintCosts(input, teams.Keys);
        var manualAdjustments = ProjectionPenalties.ManualAdjustments(input, teams.Keys);
        var rows = teams.Values.Select(team =>
        {
            var own = observations.Where(fact => fact.Event.TeamId == team.Id).ToList();
            var first = own.Select(fact => fact.Event.OccurredAt).FirstOrDefault();
            var last = own.Select(fact => fact.Event.OccurredAt).LastOrDefault();
            var observationPoints = own.Aggregate(
                0L,
                (total, fact) => checked(total + PointsForObservation(
                    configuration.ControlPointsPerInterval,
                    challengePoints,
                    fact.Event.CompetitionChallengeId!.Value)));
            return new KohRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(observationPoints - hintCosts.GetValueOrDefault(team.Id)
                        + manualAdjustments.GetValueOrDefault(team.Id)),
                    own.Count,
                    last == default ? null : last,
                    []),
                own.Count,
                own.Select(fact => fact.Event.CompetitionChallengeId!.Value).Distinct().Count(),
                first == default ? null : first,
                team.RegisteredAt);
        });
        return rows.OrderByDescending(row => row.Entry.Score)
            .ThenByDescending(row => row.ControlledObservationCount)
            .ThenByDescending(row => row.ControlledChallengeCount)
            .ThenBy(row => row.FirstControlAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.RegisteredAt)
            .ThenBy(row => row.Entry.TeamId)
            .Select((row, index) => row.Entry with { Rank = index + 1 })
            .ToList();
    }

    private static long PointsFor(KohConfiguration competition, string? challengeJson) =>
        ParseChallenge(challengeJson).ControlPointsPerInterval
        ?? competition.ControlPointsPerInterval;

    private static long PointsForObservation(
        long competitionDefault,
        IReadOnlyDictionary<Guid, long> challengePoints,
        Guid challengeId) =>
        challengePoints.GetValueOrDefault(challengeId, competitionDefault);

    private static KohConfiguration ParseCompetition(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(1, 5, 10);
        try
        {
            return JsonSerializer.Deserialize<KohConfiguration>(json, JsonOptions)
                ?? new(1, 5, 10);
        }
        catch (JsonException)
        {
            return new(1, 5, 10);
        }
    }

    private static KohChallengeConfiguration ParseChallenge(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new(KohChallengeConfiguration.CurrentSchemaVersion);
        try
        {
            return JsonSerializer.Deserialize<KohChallengeConfiguration>(json, JsonOptions)
                ?? new(KohChallengeConfiguration.CurrentSchemaVersion);
        }
        catch (JsonException)
        {
            return new(KohChallengeConfiguration.CurrentSchemaVersion);
        }
    }

    private sealed record KohRankedEntry(
        LeaderboardEntry Entry,
        int ControlledObservationCount,
        int ControlledChallengeCount,
        DateTimeOffset? FirstControlAt,
        DateTimeOffset RegisteredAt);
}

internal static class ProjectionPenalties
{
    public static IReadOnlyList<(Guid TeamId, DateTimeOffset OccurredAt, Guid EventId, long Cost)> HintCostEvents(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
        var submissionEvents = input.Submissions
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Result: ScoringResult.Correct,
                Kind: ScoringEventKind.HintUnlock,
                TeamId: not null
            } && validTeams.Contains(fact.TeamId))
            .Select(fact => (fact.TeamId, fact.Event.OccurredAt, fact.Event.Id,
                Cost: fact.HintCost ?? 0))
            .ToList();
        if (submissionEvents.Count > 0)
            return submissionEvents;
        return input.SystemEvents
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Kind: ScoringEventKind.HintUnlock,
                TeamId: not null
            } && validTeams.Contains(fact.Event.TeamId!.Value))
            .Select(fact => (fact.Event.TeamId!.Value, fact.Event.OccurredAt, fact.Event.Id, fact.CurrentValue))
            .ToList();
    }

    public static IReadOnlyDictionary<Guid, long> HintCosts(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
        var submissionHints = input.Submissions
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Result: ScoringResult.Correct,
                Kind: ScoringEventKind.HintUnlock,
                TeamId: not null
            } && validTeams.Contains(fact.TeamId))
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + (fact.HintCost ?? 0))));
        if (submissionHints.Count > 0)
            return submissionHints;
        return input.SystemEvents
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Kind: ScoringEventKind.HintUnlock,
                TeamId: not null
            } && validTeams.Contains(fact.Event.TeamId!.Value))
            .GroupBy(fact => fact.Event.TeamId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + fact.CurrentValue)));
    }

    public static IReadOnlyDictionary<Guid, long> ManualAdjustments(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
        return input.Submissions
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Result: ScoringResult.Correct,
                Kind: ScoringEventKind.ManualAdjust,
                TeamId: not null
            } && validTeams.Contains(fact.TeamId))
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + ParseDelta(fact.SubmittedFlag))));
    }

    public static IReadOnlyList<(Guid TeamId, DateTimeOffset OccurredAt, Guid EventId, long Delta)> ManualAdjustmentEvents(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
        return input.Submissions
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Result: ScoringResult.Correct,
                Kind: ScoringEventKind.ManualAdjust,
                TeamId: not null
            } && validTeams.Contains(fact.TeamId))
            .Select(fact => (fact.TeamId, fact.Event.OccurredAt, fact.Event.Id,
                Delta: ParseDelta(fact.SubmittedFlag)))
            .ToList();
    }

    private static long ParseDelta(string? value) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)
            ? delta
            : 0;
}
