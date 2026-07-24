using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using System.Text.Json;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Ctf.Scoring;

namespace NoCTF.GameModes.Leaderboard;

public sealed class CtfLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Ctf;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        CtfLeaderboardProjection.Project(input);
}

internal static class CtfLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CtfScoreExpression ScoreExpression = new();
    private const string DefaultScoreExpression =
        "solveCount <= 1 ? initialPoints : solveCount >= decayParameter ? minimumPoints : initialPoints + (minimumPoints - initialPoints) * ((solveCount - 1m) / (decayParameter - 1m)) * ((solveCount - 1m) / (decayParameter - 1m))";

    public static IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
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

        var awarded = new Dictionary<Guid, List<(LeaderboardSubmissionFact Fact, long Points)>>();
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
            teamSolves.Add((solve, score));
        }

        var wrongPenalties = input.Submissions
            .Where(fact => validTeams.ContainsKey(fact.TeamId)
                && fact.Kind == SubmissionKind.Flag
                && fact.Event is { DeletedAt: null, Result: ScoringResult.Wrong })
            .GroupBy(fact => fact.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) =>
                {
                    var challenge = fact.CompetitionChallengeId is Guid challengeId
                        ? ParseChallenge(challenges.GetValueOrDefault(challengeId)?.ConfigurationJson)
                        : null;
                    var penalty = challenge?.WrongSubmissionPenalty ?? defaults.WrongSubmissionPenalty;
                    return checked(total + penalty);
                }));
        var hintCosts = ProjectionPenalties.HintCosts(input, validTeams.Keys);

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
            var last = own.Select(item => item.Fact.Event.OccurredAt).OrderByDescending(value => value).FirstOrDefault();
            var total = checked(own.Aggregate(0L, (sum, item) => checked(sum + item.Points))
                - wrongPenalties.GetValueOrDefault(team.Id)
                - hintCosts.GetValueOrDefault(team.Id));
            return new LeaderboardEntry(0, team.Id, team.Name, total, own.Count,
                last == default ? null : last, summaries);
        });
        return rows
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.LastScoreAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private static long BloodRewardAt(
        IReadOnlyList<BloodReward> rewards,
        int solveIndex,
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
}

public sealed class AwdLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Awd;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        AwdLeaderboardProjection.Project(input);
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
                values[attacker] = checked(values[attacker] + reward);
            values[pool.Key.VictimId] = checked(
                values[pool.Key.VictimId] - settings.VictimDefensePoolPoints);
        }
        // AwdServiceStatus is a state-change fact, not a score delta. Service points are
        // projected only after complete round windows are supplied to the projection input.
        foreach (var hint in ProjectionPenalties.HintCosts(input, teams.Keys))
            values[hint.Key] = checked(values[hint.Key] - hint.Value);
        var rows = teams.Values.Select(team => new LeaderboardEntry(
            0,
            team.Id,
            team.Name,
            values[team.Id],
            attacks.Count(solve => solve.TeamId == team.Id),
            LastSolve(attacks, team.Id),
            []));
        return rows.OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.LastScoreAt)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private static DateTimeOffset? LastSolve(IReadOnlyList<LeaderboardSubmissionFact> solves, Guid teamId)
    {
        var last = solves.Where(solve => solve.TeamId == teamId)
            .Select(solve => solve.Event.OccurredAt)
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
}

public sealed class AwdpLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Awdp;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        AwdpLeaderboardProjection.Project(input);
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
                : Round(fact.ReceivedAt, input.CompetitionStartTime, competition.RoundDurationSeconds);
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
        var rows = teams.Values.Select(team =>
        {
            var own = awarded.GetValueOrDefault(team.Id) ?? [];
            var last = own.Select(item => item.Fact.Event.OccurredAt).OrderByDescending(value => value).FirstOrDefault();
            var lastFixAt = own
                .Where(item => item.Fact.Kind == SubmissionKind.Fix)
                .Select(item => (DateTimeOffset?)item.Fact.Event.OccurredAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var awardedScore = own.Aggregate(0L, (total, item) => checked(total + item.Points));
            var penalty = penalties.GetValueOrDefault(team.Id);
            return new AwdpRankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(awardedScore - penalty - hintCosts.GetValueOrDefault(team.Id)),
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

    private static int Round(DateTimeOffset occurredAt, DateTimeOffset? start, int durationSeconds)
    {
        if (start is null || durationSeconds <= 0) return 1;
        var seconds = Math.Max(0, (occurredAt - start.Value).TotalSeconds);
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

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        KohLeaderboardProjection.Project(input);
}

internal static class KohLeaderboardProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
    {
        var configuration = Parse(input.CompetitionConfigurationJson);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var observations = input.SystemEvents
            .Where(fact => fact.Event is
            {
                DeletedAt: null,
                Kind: ScoringEventKind.KohObservation,
                Result: ScoringResult.Correct,
                TeamId: not null
            } && teams.ContainsKey(fact.Event.TeamId!.Value))
            .OrderBy(fact => fact.Event.OccurredAt)
            .ThenBy(fact => fact.Event.Id)
            .ToList();
        var hintCosts = ProjectionPenalties.HintCosts(input, teams.Keys);
        var rows = teams.Values.Select(team =>
        {
            var own = observations.Where(fact => fact.Event.TeamId == team.Id).ToList();
            var last = own.Select(fact => fact.Event.OccurredAt).LastOrDefault();
            return new LeaderboardEntry(0, team.Id, team.Name,
                checked(own.Count * configuration.ControlPointsPerInterval
                    - hintCosts.GetValueOrDefault(team.Id)),
                0,
                last == default ? null : last,
                []);
        });
        return rows.OrderByDescending(row => row.Score)
            .ThenBy(row => row.LastScoreAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private static KohConfiguration Parse(string? json)
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
}

internal static class ProjectionPenalties
{
    public static IReadOnlyDictionary<Guid, long> HintCosts(
        LeaderboardProjectionInput input,
        IEnumerable<Guid> teamIds)
    {
        var validTeams = teamIds.ToHashSet();
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
}
