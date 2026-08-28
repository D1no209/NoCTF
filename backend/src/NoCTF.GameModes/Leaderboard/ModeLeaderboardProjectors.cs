using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using System.Text.Json;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;
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
    private static readonly ScoreCurveEvaluator ScoreCurve = new();

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
                var curve = configuration.ScoreCurve ?? defaults.DefaultScoreCurve;
                var solveCount = Math.Max(1, currentSolveCounts.GetValueOrDefault(pair.Key));
                return ScoreCurve.Evaluate(curve, solveCount, dynamicTeams.Count);
            });

        var awarded = new Dictionary<Guid, List<(LeaderboardGameplayFact Fact, long Points, int SolveOrdinal)>>();
        var bloodSolveNumber = new Dictionary<Guid, int>();
        foreach (var solve in solves)
        {
            var challengeId = solve.CompetitionChallengeId!.Value;
            var configuration = ParseChallenge(challenges.TryGetValue(challengeId, out var challenge)
                ? challenge.ConfigurationJson
                : null);
            var curve = configuration.ScoreCurve ?? defaults.DefaultScoreCurve;
            if (!validTeams.TryGetValue(solve.TeamId!.Value, out var team))
                continue;
            var bloodIndex = bloodSolveNumber.GetValueOrDefault(challengeId);
            var solveOrdinal = team.EarnsBlood ? checked(bloodIndex + 1) : 0;
            var score = ScoreCurve.Evaluate(
                curve,
                Math.Max(1, currentSolveCounts.GetValueOrDefault(challengeId)),
                dynamicTeams.Count);
            if (team.EarnsBlood)
            {
                score = checked(score + BloodRewardAt(
                    configuration.BloodRewards ?? defaults.BloodRewards,
                    bloodIndex,
                    score,
                    () => currentSolveCounts.GetValueOrDefault(challengeId) == solveOrdinal
                        ? score
                        : ScoreCurve.Evaluate(curve, solveOrdinal, dynamicTeams.Count),
                    curve));
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
                fact.Multiplicity,
                Penalty: (fact.CompetitionChallengeId is Guid challengeId
                        ? ParseChallenge(challenges.GetValueOrDefault(challengeId)?.ConfigurationJson)
                        : null)
                    ?.WrongSubmissionPenalty ?? defaults.WrongSubmissionPenalty))
            .ToList();
        var wrongPenalties = wrongFacts
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(
                    total + fact.Penalty * fact.Multiplicity)));
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
        ScoreCurveConfiguration curve)
    {
        if ((uint)solveIndex >= (uint)rewards.Count)
            return 0;

        var reward = rewards[solveIndex];
        var basis = reward.Policy switch
        {
            BloodRewardPolicy.FixedPoints => reward.Value,
            BloodRewardPolicy.InitialPointsPercentage => curve.InitialPoints * reward.Value / 100m,
            BloodRewardPolicy.SolveTimePointsPercentage => solvePoints() * reward.Value / 100m,
            BloodRewardPolicy.CurrentPointsPercentage => currentPoints * reward.Value / 100m,
            _ => 0m
        };
        return checked((long)Math.Round(basis, MidpointRounding.AwayFromZero));
    }

    private static CtfConfiguration ParseCompetition(string? json) =>
        TryParse<CtfConfiguration>(json) ?? new(
            CtfConfiguration.CurrentSchemaVersion,
            ScoreCurveConfiguration.Default,
            []);

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
        if (input.AwdAggregates is not null)
            return ProjectAggregates(input, challenges, teams);
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
        var projectedAt = input.ProjectedAt
            ?? throw new InvalidOperationException("Leaderboard projection time is required.");
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

    private static (IReadOnlyList<LeaderboardEntry> Entries, IReadOnlyList<LeaderboardCellFact> Cells)
        ProjectAggregates(
            LeaderboardProjectionInput input,
            IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges,
            IReadOnlyDictionary<Guid, LeaderboardTeamFact> teams)
    {
        var aggregates = input.AwdAggregates!
            .Where(fact => teams.ContainsKey(fact.TeamId)
                && challenges.ContainsKey(fact.CompetitionChallengeId))
            .GroupBy(fact => (fact.TeamId, fact.CompetitionChallengeId))
            .Select(group => new LeaderboardAwdAggregateFact(
                group.Key.TeamId,
                group.Key.CompetitionChallengeId,
                group.Aggregate(0L, (total, fact) => checked(total + fact.Score)),
                group.Aggregate(0L, (total, fact) => checked(total + fact.AttackPoints)),
                group.Sum(fact => fact.AttackCount),
                group.Sum(fact => fact.UpRoundCount),
                group.Max(fact => fact.LastAttackAt)))
            .ToArray();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, aggregates
            .Where(fact => fact.Score != 0 || fact.AttackCount != 0 || fact.UpRoundCount != 0)
            .Select(fact => new LeaderboardCellFact(
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Score,
                fact.LastAttackAt,
                null))
            .ToArray());
        var cellsByTeam = cells
            .GroupBy(cell => cell.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(cell => cell.CompetitionChallengeId));
        var attackPoints = aggregates
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + fact.AttackPoints)));
        var attackCounts = aggregates
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(group => group.Key, group => group.Sum(fact => fact.AttackCount));
        var upRoundCounts = aggregates
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(group => group.Key, group => group.Sum(fact => fact.UpRoundCount));
        var lastAttackAt = aggregates
            .Where(fact => fact.LastAttackAt is not null)
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(group => group.Key, group => group.Max(fact => fact.LastAttackAt));
        var rows = teams.Values.Select(team =>
        {
            var teamCells = cellsByTeam.GetValueOrDefault(team.Id, []);
            var score = teamCells.Values.Aggregate(0L, (total, cell) => checked(total + cell.Score));
            var attackCount = attackCounts.GetValueOrDefault(team.Id);
            return new AwdRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    score,
                    attackCount,
                    lastAttackAt.GetValueOrDefault(team.Id),
                    team.TrackKey),
                attackPoints.GetValueOrDefault(team.Id),
                upRoundCounts.GetValueOrDefault(team.Id),
                attackCount,
                lastAttackAt.GetValueOrDefault(team.Id),
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
                .Select((row, index) => row.Entry with
                {
                    Rank = index + 1,
                    Cells = cellsByTeam.GetValueOrDefault(row.Entry.TeamId, [])
                        .Values
                        .OrderBy(cell => challenges[cell.CompetitionChallengeId].Order)
                        .ThenBy(cell => cell.CompetitionChallengeId)
                        .Select(cell => new LeaderboardCell(
                            cell.CompetitionChallengeId,
                            cell.Score,
                            cell.SolvedAt,
                            cell.SolverName,
                            null))
                        .ToArray()
                }))
            .ToArray();
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
        => AwdpDynamicLeaderboardProjection.Project(input);
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
                    fact.CompetitionChallengeId!.Value) * fact.Multiplicity));
            var observationCount = own.Aggregate(
                0,
                (total, fact) => checked(total + fact.Multiplicity));
            return new KohRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(observationPoints - hintCosts.GetValueOrDefault(team.Id)
                        + manualAdjustments.GetValueOrDefault(team.Id)),
                    observationCount,
                    last == default ? null : own.Max(fact => fact.LastOccurredAt ?? fact.OccurredAt),
                    team.TrackKey),
                observationCount,
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
                        fact.CompetitionChallengeId!.Value) * fact.Multiplicity)),
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
                    (total, fact) => checked(total + ParseDelta(fact.Value) * fact.Multiplicity)));
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
                group => group.Aggregate(0L, (total, fact) => checked(
                    total + (fact.HintCost ?? 0) * fact.Multiplicity)));
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
                group => group.Aggregate(0L, (total, fact) => checked(
                    total + ParseDelta(fact.Value) * fact.Multiplicity)));
    }

    internal static long ParseDelta(string? value) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)
            ? delta
            : 0;
}
