using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
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
        var (entries, cells, currentScores) = ProjectCore(input);
        return new(entries, cells, currentScores);
    }

    private static (
        IReadOnlyList<LeaderboardEntry> Entries,
        IReadOnlyList<LeaderboardCellFact> Cells,
        IReadOnlyDictionary<Guid, long> CurrentScores)
        ProjectCore(LeaderboardProjectionInput input)
    {
        var activeTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .ToDictionary(team => team.Id);
        var validTeams = activeTeams.Values
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .ToDictionary(team => team.Id);
        var dynamicTeams = activeTeams.Values
            .Where(team => team.AffectsDynamicChallengeScore)
            .Select(team => team.Id)
            .ToHashSet();
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted).ToDictionary(challenge => challenge.Id);
        var defaults = ParseCompetition(input.CompetitionConfigurationJson);
        var solves = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && activeTeams.ContainsKey(teamId)
                          && fact.Kind == GameplayFactKind.FlagAttempt
                          && fact.Result == GameplayFactResult.Correct
                          && fact.CompetitionChallengeId is not null
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .GroupBy(fact => (fact.TeamId, fact.CompetitionChallengeId))
            .Select(group => group.First())
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToList();
        var currentSolveCounts = solves
            .Where(solve => dynamicTeams.Contains(solve.TeamId!.Value))
            .GroupBy(solve => solve.CompetitionChallengeId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var currentScores = challenges.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var configuration = ParseChallenge(pair.Value.ConfigurationJson);
                var points = configuration.Points ?? defaults.DefaultPoints;
                var expression = configuration.ScoreExpression
                    ?? defaults.ScoreExpression
                    ?? DefaultScoreExpression;
                var solveCount = Math.Max(1, currentSolveCounts.GetValueOrDefault(pair.Key));
                return ScoreExpression.Evaluate(expression, new(
                    points.InitialPoints,
                    points.MinimumPoints,
                    solveCount,
                    dynamicTeams.Count,
                    points.DecayFactor));
            });

        var awarded = new Dictionary<Guid, List<(LeaderboardGameplayFact Fact, long Points, int SolveOrdinal)>>();
        var bloodSolveNumber = new Dictionary<Guid, int>();
        foreach (var solve in solves)
        {
            var challengeId = solve.CompetitionChallengeId!.Value;
            var configuration = ParseChallenge(challenges.TryGetValue(challengeId, out var challenge)
                ? challenge.ConfigurationJson
                : null);
            var points = configuration.Points ?? defaults.DefaultPoints;
            if (!validTeams.TryGetValue(solve.TeamId!.Value, out var team))
                continue;
            var bloodIndex = bloodSolveNumber.GetValueOrDefault(challengeId);
            var solveOrdinal = team.EarnsBlood ? checked(bloodIndex + 1) : 0;
            var expression = configuration.ScoreExpression
                ?? defaults.ScoreExpression
                ?? DefaultScoreExpression;
            var score = ScoreExpression.Evaluate(expression, new(
                points.InitialPoints,
                points.MinimumPoints,
                Math.Max(1, currentSolveCounts.GetValueOrDefault(challengeId)),
                dynamicTeams.Count,
                points.DecayFactor));
            if (team.EarnsBlood)
            {
                score = checked(score + BloodRewardAt(
                    configuration.BloodRewards ?? defaults.BloodRewards,
                    bloodIndex,
                    score,
                    () => currentSolveCounts.GetValueOrDefault(challengeId) == solveOrdinal
                        ? score
                        : ScoreExpression.Evaluate(expression, new(
                            points.InitialPoints,
                            points.MinimumPoints,
                            solveOrdinal,
                            dynamicTeams.Count,
                            points.DecayFactor)),
                    points));
                bloodSolveNumber[challengeId] = bloodIndex + 1;
            }
            var teamId = solve.TeamId!.Value;
            if (!awarded.TryGetValue(teamId, out var teamSolves))
                awarded[teamId] = teamSolves = [];
            teamSolves.Add((solve, score, solveOrdinal));
        }

        var wrongFacts = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && validTeams.ContainsKey(teamId)
                && fact.Kind == GameplayFactKind.FlagAttempt
                && fact.Result == GameplayFactResult.Wrong)
            .Select(fact =>
                (TeamId: fact.TeamId!.Value,
                fact.OccurredAt,
                fact.GameplayFactId,
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
            var last = own.Select(item => item.Fact.OccurredAt)
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
                    team.TrackKey),
                team.RegisteredAt);
        });
        var entries = rows
            .GroupBy(row => row.Entry.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => track
                .OrderByDescending(row => row.Entry.Score)
                .ThenBy(row => row.Entry.LastScoreAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(row => row.Entry.SolveCount)
                .ThenBy(row => row.RegisteredAt)
                .ThenBy(row => row.Entry.TeamId)
                .Select((row, index) => row.Entry with { Rank = index + 1 }))
            .ToList();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, awarded
            .SelectMany(pair => pair.Value.Select(item => new LeaderboardCellFact(
                pair.Key,
                item.Fact.CompetitionChallengeId!.Value,
                item.Points,
                item.Fact.OccurredAt,
                string.IsNullOrWhiteSpace(item.Fact.SubmitterName) ? null : item.Fact.SubmitterName)))
            .ToList());
        return (entries, cells, currentScores);
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
    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input)
    {
        var projection = AwdLeaderboardProjection.Project(input);
        return new(projection.Entries, projection.Cells);
    }
}

internal static class AwdLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static (IReadOnlyList<LeaderboardEntry> Entries, IReadOnlyList<LeaderboardCellFact> Cells) Project(LeaderboardProjectionInput input)
    {
        var configuration = TryParse(input.CompetitionConfigurationJson)
            ?? AwdConfiguration.Default;
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var teams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .ToDictionary(team => team.Id);
        var competitiveTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.AffectsCompetitiveResults)
            .ToDictionary(team => team.Id);
        var values = teams.Keys.ToDictionary(team => team, _ => 0L);
        var cellScores = new Dictionary<(Guid TeamId, Guid ChallengeId), long>();
        var cellSolves = new Dictionary<(Guid TeamId, Guid ChallengeId), (DateTimeOffset At, string? SolverName)>();
        void AddCell(
            Guid teamId,
            Guid challengeId,
            long delta,
            DateTimeOffset? solvedAt = null,
            string? solverName = null)
        {
            var key = (teamId, challengeId);
            cellScores[key] = checked(cellScores.GetValueOrDefault(key) + delta);
            if (solvedAt is { } at
                && (!cellSolves.TryGetValue(key, out var current) || at < current.At))
                cellSolves[key] = (at, string.IsNullOrWhiteSpace(solverName) ? null : solverName);
        }
        var attackPoints = teams.Keys.ToDictionary(team => team, _ => 0L);
        var upRoundCounts = teams.Keys.ToDictionary(team => team, _ => 0);
        var solves = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && competitiveTeams.ContainsKey(teamId)
                          && fact.Kind == GameplayFactKind.FlagAttempt
                          && fact is
                          {
                              Result: GameplayFactResult.Correct,
                              ReferenceKind: GameplayFactReferenceKind.AwdRound,
                              ReferenceId: not null
                          }
                          && fact.CompetitionChallengeId is not null
                          && fact.VictimTeamId is not null
                          && fact.VictimTeamId != fact.TeamId
                          && competitiveTeams.ContainsKey(fact.VictimTeamId.Value)
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToList();
        var attacks = solves
            .GroupBy(fact => new
            {
                ChallengeId = fact.CompetitionChallengeId!.Value,
                RoundId = fact.ReferenceId!.Value,
                VictimId = fact.VictimTeamId!.Value,
                AttackerId = fact.TeamId!.Value
            })
            .Select(group => group.First())
            .ToList();
        foreach (var pool in attacks.GroupBy(fact => new
        {
            ChallengeId = fact.CompetitionChallengeId!.Value,
            RoundId = fact.ReferenceId!.Value,
            VictimId = fact.VictimTeamId!.Value
        }))
        {
            var settings = Effective(configuration, challenges.GetValueOrDefault(pool.Key.ChallengeId)?.ConfigurationJson);
            var attackers = pool.Select(fact => fact.TeamId!.Value).Distinct().ToList();
            var reward = settings.AttackRewardMode == AttackRewardMode.FixedPerAttack
                ? settings.AttackPoints
                : settings.VictimDefensePoolPoints / attackers.Count;
            foreach (var attacker in attackers)
            {
                if (!values.ContainsKey(attacker))
                    continue;
                values[attacker] = checked(values[attacker] + reward);
                attackPoints[attacker] = checked(attackPoints[attacker] + reward);
                var first = pool.First(fact => fact.TeamId == attacker);
                AddCell(attacker, pool.Key.ChallengeId, reward, first.OccurredAt, first.SubmitterName);
            }
            if (values.ContainsKey(pool.Key.VictimId))
            {
                values[pool.Key.VictimId] = checked(
                    values[pool.Key.VictimId] - settings.VictimDefensePoolPoints);
                AddCell(pool.Key.VictimId, pool.Key.ChallengeId, -settings.VictimDefensePoolPoints);
            }
        }
        var serviceStates = input.GameplayFacts
            .Where(fact => fact is
            {
                Kind: GameplayFactKind.AwdServiceTransition,
                TeamId: not null,
                CompetitionChallengeId: not null,
                Result: GameplayFactResult.ServiceUp or GameplayFactResult.ServiceDown
            })
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
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
                .Where(fact => fact.TeamId == round.TeamId
                    && fact.CompetitionChallengeId == round.CompetitionChallengeId
                    && fact.OccurredAt < round.EndsAt)
                .LastOrDefault();
            var settings = Effective(
                configuration,
                challenges[round.CompetitionChallengeId].ConfigurationJson);
            if (latestState?.Result == GameplayFactResult.ServiceDown)
            {
                values[round.TeamId] = checked(
                    values[round.TeamId] - settings.ServiceUnhealthyPenalty);
                AddCell(round.TeamId, round.CompetitionChallengeId, -settings.ServiceUnhealthyPenalty);
            }
            else
            {
                values[round.TeamId] = checked(
                    values[round.TeamId] + settings.ServiceHealthyPoints);
                AddCell(round.TeamId, round.CompetitionChallengeId, settings.ServiceHealthyPoints);
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
                    team.TrackKey),
                attackPoints[team.Id],
                upRoundCounts[team.Id],
                attackCount,
                lastAttackAt,
                team.RegisteredAt);
        });
        var entries = rows
            .GroupBy(row => row.Entry.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => track
                .OrderByDescending(row => row.Entry.Score)
                .ThenByDescending(row => row.AttackPoints)
                .ThenByDescending(row => row.UpRoundCount)
                .ThenByDescending(row => row.AttackCount)
                .ThenBy(row => row.LastAttackAt ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.RegisteredAt)
                .ThenBy(row => row.Entry.TeamId)
                .Select((row, index) => row.Entry with { Rank = index + 1 }))
            .ToList();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, cellScores.Select(pair =>
        {
            var solve = cellSolves.GetValueOrDefault(pair.Key);
            return new LeaderboardCellFact(
                pair.Key.TeamId,
                pair.Key.ChallengeId,
                pair.Value,
                solve.At == default ? null : solve.At,
                solve.SolverName);
        }).ToList());
        return (entries, cells);
    }

    private static DateTimeOffset? LastAttackAt(
        IReadOnlyList<LeaderboardGameplayFact> attacks,
        Guid teamId)
    {
        var last = attacks.Where(attack => attack.TeamId == teamId)
            .Select(attack => attack.OccurredAt)
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
    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input)
    {
        var projection = AwdpLeaderboardProjection.Project(input);
        return new(projection.Entries, projection.Cells);
    }
}

internal static class AwdpLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static (IReadOnlyList<LeaderboardEntry> Entries, IReadOnlyList<LeaderboardCellFact> Cells) Project(LeaderboardProjectionInput input)
    {
        var competition = ParseCompetition(input.CompetitionConfigurationJson);
        if (competition.UsesContinuousRoundScoring)
            return ProjectContinuous(input, competition);
        var teams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .ToDictionary(team => team.Id);
        var competitiveTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToHashSet();
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted).ToDictionary(challenge => challenge.Id);
        var facts = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && teams.ContainsKey(teamId)
                          && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                          && fact.Result == GameplayFactResult.Correct
                          && fact.CompetitionChallengeId is not null
                          && (fact.Kind != GameplayFactKind.BreakAttempt
                              || competitiveTeams.Contains(teamId)
                              && (fact.VictimTeamId is null
                                  || competitiveTeams.Contains(fact.VictimTeamId.Value)))
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToList();
        var correctBreaks = facts
            .Where(fact => fact.Kind == GameplayFactKind.BreakAttempt)
            .Select(fact => (fact.TeamId, fact.CompetitionChallengeId!.Value))
            .ToHashSet();
        var awarded = new Dictionary<Guid, List<(LeaderboardGameplayFact Fact, long Points)>>();
        var milestones = new HashSet<(Guid TeamId, Guid ChallengeId, GameplayFactKind Kind, int Round)>();
        foreach (var fact in facts)
        {
            var challengeId = fact.CompetitionChallengeId!.Value;
            var configuration = Effective(
                competition,
                challenges.GetValueOrDefault(challengeId)?.ConfigurationJson);
            if (fact.Kind == GameplayFactKind.FixAttempt
                && configuration.RequireBreakBeforeFix
                && !correctBreaks.Contains((fact.TeamId, challengeId)))
                continue;
            var achievement = fact.Kind == GameplayFactKind.BreakAttempt
                ? configuration.Break
                : configuration.Fix;
            var round = achievement.Settlement == AchievementSettlement.Milestone
                ? 0
                : Round(
                    fact.OccurredAt,
                    input.LifecycleAudits,
                    input.CompetitionStartTime,
                    competition.RoundDurationSeconds);
            var teamId = fact.TeamId!.Value;
            if (!milestones.Add((teamId, challengeId, fact.Kind, round)))
                continue;
            if (!awarded.TryGetValue(teamId, out var teamFacts))
                awarded[teamId] = teamFacts = [];
            teamFacts.Add((fact, achievement.Points));
        }
        var penalties = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && teams.ContainsKey(teamId)
                           && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                           && (fact.Kind != GameplayFactKind.BreakAttempt
                               || competitiveTeams.Contains(teamId)
                               && (fact.VictimTeamId is null
                                   || competitiveTeams.Contains(fact.VictimTeamId.Value)))
                           && fact.CompetitionChallengeId is not null
                           && (challenges.Count == 0 || challenges.ContainsKey(fact.CompetitionChallengeId.Value)))
            .GroupBy(fact => fact.TeamId!.Value)
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
            var last = own.Select(item => item.Fact.OccurredAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var lastFixAt = own
                .Where(item => item.Fact.Kind == GameplayFactKind.FixAttempt)
                .Select(item => (DateTimeOffset?)item.Fact.OccurredAt)
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
                    team.TrackKey),
                own.Count(item => item.Fact.Kind == GameplayFactKind.FixAttempt),
                own.Count(item => item.Fact.Kind == GameplayFactKind.BreakAttempt),
                penalty,
                lastFixAt,
                team.RegisteredAt);
        });
        var entries = rows
            .GroupBy(row => row.Entry.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => track
                .OrderByDescending(row => row.Entry.Score)
                .ThenByDescending(row => row.FixCount)
                .ThenByDescending(row => row.BreakCount)
                .ThenBy(row => row.Penalty)
                .ThenBy(row => row.LastFixAt ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.RegisteredAt)
                .ThenBy(row => row.Entry.TeamId)
                .Select((row, index) => row.Entry with { Rank = index + 1 }))
            .ToList();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, awarded
            .SelectMany(pair => pair.Value
                .GroupBy(item => item.Fact.CompetitionChallengeId!.Value)
                .Select(group =>
                {
                    var first = group.OrderBy(item => item.Fact.OccurredAt).First();
                    return new LeaderboardCellFact(
                        pair.Key,
                        group.Key,
                        group.Aggregate(0L, (total, item) => checked(total + item.Points)),
                        first.Fact.OccurredAt,
                        string.IsNullOrWhiteSpace(first.Fact.SubmitterName) ? null : first.Fact.SubmitterName);
                }))
            .ToList());
        return (entries, cells);
    }

    private static (
        IReadOnlyList<LeaderboardEntry> Entries,
        IReadOnlyList<LeaderboardCellFact> Cells) ProjectContinuous(
        LeaderboardProjectionInput input,
        AwdpConfiguration competition)
    {
        var teams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .ToDictionary(team => team.Id);
        var competitiveTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToHashSet();
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var projectedAt = input.ProjectedAt ?? DateTimeOffset.UtcNow;
        var currentRound = Round(
            projectedAt,
            input.LifecycleAudits,
            input.CompetitionStartTime,
            competition.RoundDurationSeconds);
        var activations = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId
                && teams.ContainsKey(teamId)
                && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                && fact.Result == GameplayFactResult.Correct
                && fact.CompetitionChallengeId is Guid challengeId
                && (challenges.Count == 0 || challenges.ContainsKey(challengeId))
                && (fact.Kind != GameplayFactKind.BreakAttempt
                    || competitiveTeams.Contains(teamId)
                    && (fact.VictimTeamId is null
                        || competitiveTeams.Contains(fact.VictimTeamId.Value))))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .GroupBy(fact => new
            {
                TeamId = fact.TeamId!.Value,
                ChallengeId = fact.CompetitionChallengeId!.Value,
                fact.Kind
            })
            .Select(group => group.First())
            .Select(fact =>
            {
                var challengeId = fact.CompetitionChallengeId!.Value;
                var configuration = Effective(
                    competition,
                    challenges.GetValueOrDefault(challengeId)?.ConfigurationJson);
                var activationRound = Round(
                    fact.OccurredAt,
                    input.LifecycleAudits,
                    input.CompetitionStartTime,
                    competition.RoundDurationSeconds);
                var activeRounds = checked(Math.Max(0, currentRound - activationRound + 1));
                var pointsPerRound = fact.Kind == GameplayFactKind.BreakAttempt
                    ? configuration.Break.Points
                    : configuration.Fix.Points;
                return new ContinuousAward(
                    fact,
                    checked(pointsPerRound * activeRounds),
                    activationRound,
                    currentRound);
            })
            .ToList();
        var awardsByTeam = activations
            .GroupBy(item => item.Fact.TeamId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var penalties = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && teams.ContainsKey(teamId)
                && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                && fact.CompetitionChallengeId is Guid challengeId
                && (challenges.Count == 0 || challenges.ContainsKey(challengeId)))
            .GroupBy(fact => fact.TeamId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total +
                    PenaltyFor(
                        fact,
                        Effective(
                            competition,
                            challenges.GetValueOrDefault(
                                fact.CompetitionChallengeId!.Value)?.ConfigurationJson)))));
        var hintCosts = ProjectionPenalties.HintCosts(input, teams.Keys);
        var manualAdjustments = ProjectionPenalties.ManualAdjustments(input, teams.Keys);
        var rows = teams.Values.Select(team =>
        {
            var own = awardsByTeam.GetValueOrDefault(team.Id) ?? [];
            var last = own.Select(item => item.Fact.OccurredAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var lastFixAt = own
                .Where(item => item.Fact.Kind == GameplayFactKind.FixAttempt)
                .Select(item => (DateTimeOffset?)item.Fact.OccurredAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var awardScore = own.Aggregate(
                0L,
                (total, item) => checked(total + item.Points));
            var penalty = penalties.GetValueOrDefault(team.Id);
            return new AwdpRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(awardScore - penalty - hintCosts.GetValueOrDefault(team.Id)
                        + manualAdjustments.GetValueOrDefault(team.Id)),
                    own.Count,
                    last == default ? null : last,
                    team.TrackKey),
                own.Count(item => item.Fact.Kind == GameplayFactKind.FixAttempt),
                own.Count(item => item.Fact.Kind == GameplayFactKind.BreakAttempt),
                penalty,
                lastFixAt,
                team.RegisteredAt);
        });
        var entries = rows
            .GroupBy(row => row.Entry.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => track
                .OrderByDescending(row => row.Entry.Score)
                .ThenByDescending(row => row.FixCount)
                .ThenByDescending(row => row.BreakCount)
                .ThenBy(row => row.Penalty)
                .ThenBy(row => row.LastFixAt ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.RegisteredAt)
                .ThenBy(row => row.Entry.TeamId)
                .Select((row, index) => row.Entry with { Rank = index + 1 }))
            .ToList();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, activations
            .GroupBy(item => new
            {
                TeamId = item.Fact.TeamId!.Value,
                ChallengeId = item.Fact.CompetitionChallengeId!.Value
            })
            .Select(group =>
            {
                var first = group.OrderBy(item => item.Fact.OccurredAt)
                    .ThenBy(item => item.Fact.GameplayFactId)
                    .First();
                return new LeaderboardCellFact(
                    group.Key.TeamId,
                    group.Key.ChallengeId,
                    group.Aggregate(0L, (total, item) => checked(total + item.Points)),
                    first.Fact.OccurredAt,
                    string.IsNullOrWhiteSpace(first.Fact.SubmitterName)
                        ? null
                        : first.Fact.SubmitterName);
            })
            .ToList());
        return (entries, cells);
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
        LeaderboardGameplayFact fact,
        AwdpEffectiveConfiguration configuration) =>
        (fact.Kind, fact.Result, fact.FailureCode) switch
        {
            (GameplayFactKind.BreakAttempt, GameplayFactResult.Wrong, _)
                => configuration.BreakWrongPenalty,
            (GameplayFactKind.FixAttempt, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpFixFailed
                or GameplayFactFailureCode.AwdpPatchFailed or GameplayFactFailureCode.AwdpPatchTimeout)
                => configuration.FixFailurePenalty,
            (GameplayFactKind.FixAttempt, GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpViolation)
                => configuration.ViolationPenalty,
            (GameplayFactKind.FixAttempt, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpServiceDown)
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

    private sealed record ContinuousAward(
        LeaderboardGameplayFact Fact,
        long Points,
        int ActivationRound,
        int ProjectedRound);

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
    public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input)
    {
        var projection = KohLeaderboardProjection.Project(input);
        return new(projection.Entries, projection.Cells);
    }
}

internal static class KohLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static (IReadOnlyList<LeaderboardEntry> Entries, IReadOnlyList<LeaderboardCellFact> Cells) Project(LeaderboardProjectionInput input)
    {
        var configuration = ParseCompetition(input.CompetitionConfigurationJson);
        var activeTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .ToDictionary(team => team.Id);
        var teams = activeTeams.Values
            .Where(team => !team.IsBanned && !team.IsDeleted
                && team.EarnsScore
                && team.AffectsCompetitiveResults)
            .ToDictionary(team => team.Id);
        var hasChallengeCatalog = input.Challenges is not null;
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var challengePoints = challenges.ToDictionary(
            item => item.Key,
            item => PointsFor(configuration, item.Value.ConfigurationJson));
        var rawObservations = input.GameplayFacts
            .Where(fact => fact is
            {
                Kind: GameplayFactKind.KohControlObservation,
                CompetitionChallengeId: not null
            }
            && (!hasChallengeCatalog
                || challenges.ContainsKey(fact.CompetitionChallengeId!.Value)))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToList();
        var observations = new List<LeaderboardGameplayFact>();
        var currentKings = new Dictionary<Guid, Guid>();
        foreach (var observation in rawObservations)
        {
            var challengeId = observation.CompetitionChallengeId!.Value;
            if (observation.Result == GameplayFactResult.Uncontrolled)
            {
                currentKings.Remove(challengeId);
                continue;
            }
            if (observation is not { Result: GameplayFactResult.Controlled, TeamId: Guid observedTeamId }
                || !activeTeams.TryGetValue(observedTeamId, out var observedTeam))
                continue;
            if (observedTeam.AffectsCompetitiveResults)
                currentKings[challengeId] = observedTeamId;
            if (!currentKings.TryGetValue(challengeId, out var effectiveKingId)
                || !teams.ContainsKey(effectiveKingId))
                continue;
            observations.Add(observation with { TeamId = effectiveKingId });
        }
        var hintCosts = ProjectionPenalties.HintCosts(input, teams.Keys);
        var manualAdjustments = ProjectionPenalties.ManualAdjustments(input, teams.Keys);
        var rows = teams.Values.Select(team =>
        {
            var own = observations.Where(fact => fact.TeamId == team.Id).ToList();
            var first = own.Select(fact => fact.OccurredAt).FirstOrDefault();
            var last = own.Select(fact => fact.OccurredAt).LastOrDefault();
            var observationPoints = own.Aggregate(
                0L,
                (total, fact) => checked(total + PointsForObservation(
                    configuration.ControlPointsPerInterval,
                    challengePoints,
                    fact.CompetitionChallengeId!.Value)));
            return new KohRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(observationPoints - hintCosts.GetValueOrDefault(team.Id)
                        + manualAdjustments.GetValueOrDefault(team.Id)),
                    own.Count,
                    last == default ? null : last,
                    team.TrackKey),
                own.Count,
                own.Select(fact => fact.CompetitionChallengeId!.Value).Distinct().Count(),
                first == default ? null : first,
                team.RegisteredAt);
        });
        var entries = rows
            .GroupBy(row => row.Entry.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => track
                .OrderByDescending(row => row.Entry.Score)
                .ThenByDescending(row => row.ControlledObservationCount)
                .ThenByDescending(row => row.ControlledChallengeCount)
                .ThenBy(row => row.FirstControlAt ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.RegisteredAt)
                .ThenBy(row => row.Entry.TeamId)
                .Select((row, index) => row.Entry with { Rank = index + 1 }))
            .ToList();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, observations
            .GroupBy(fact => (
                TeamId: fact.TeamId!.Value,
                ChallengeId: fact.CompetitionChallengeId!.Value))
            .Select(group =>
            {
                var first = group.First();
                return new LeaderboardCellFact(
                    group.Key.TeamId,
                    group.Key.ChallengeId,
                    group.Aggregate(0L, (total, fact) => checked(total + PointsForObservation(
                        configuration.ControlPointsPerInterval,
                        challengePoints,
                        fact.CompetitionChallengeId!.Value))),
                    first.OccurredAt,
                    null);
            })
            .ToList());
        return (entries, cells);
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
    public static IReadOnlyList<LeaderboardCellFact> ApplyManualAdjustments(
        LeaderboardProjectionInput input,
        IReadOnlyList<LeaderboardCellFact> projectedCells)
    {
        var validTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .Select(team => team.Id)
            .ToHashSet();
        var adjustments = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && validTeams.Contains(teamId)
                && fact.CompetitionChallengeId is not null
                && fact is
                {
                    Result: GameplayFactResult.Applied,
                    Kind: GameplayFactKind.ManualAdjustment
                })
            .GroupBy(fact => (TeamId: fact.TeamId!.Value, ChallengeId: fact.CompetitionChallengeId!.Value))
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(
                    0L,
                    (total, fact) => checked(total + ParseDelta(fact.Value))));
        var projectedKeys = projectedCells
            .Select(cell => (cell.TeamId, ChallengeId: cell.CompetitionChallengeId))
            .ToHashSet();
        return projectedCells
            .GroupBy(cell => (cell.TeamId, ChallengeId: cell.CompetitionChallengeId))
            .Select(group =>
            {
                var first = group.OrderBy(cell => cell.SolvedAt ?? DateTimeOffset.MaxValue).First();
                return first with
                {
                    Score = checked(group.Aggregate(0L, (total, cell) => checked(total + cell.Score))
                        + adjustments.GetValueOrDefault(group.Key))
                };
            })
            .Concat(adjustments
                .Where(pair => !projectedKeys.Contains(pair.Key))
                .Select(pair => new LeaderboardCellFact(
                    pair.Key.TeamId,
                    pair.Key.ChallengeId,
                    pair.Value,
                    null,
                    null)))
            .ToList();
    }

    public static IReadOnlyDictionary<Guid, long> HintCosts(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
        var submissionHints = input.GameplayFacts
            .Where(fact => fact is
            {
                Result: GameplayFactResult.Unlocked,
                Kind: GameplayFactKind.HintUnlock,
                TeamId: not null
            } && validTeams.Contains(fact.TeamId!.Value))
            .GroupBy(fact => fact.TeamId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + (fact.HintCost ?? 0))));
        return submissionHints;
    }

    public static IReadOnlyDictionary<Guid, long> ManualAdjustments(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
        return input.GameplayFacts
            .Where(fact => fact is
            {
                Result: GameplayFactResult.Applied,
                Kind: GameplayFactKind.ManualAdjustment,
                TeamId: not null
            } && validTeams.Contains(fact.TeamId!.Value))
            .GroupBy(fact => fact.TeamId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + ParseDelta(fact.Value))));
    }

    internal static long ParseDelta(string? value) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)
            ? delta
            : 0;
}
