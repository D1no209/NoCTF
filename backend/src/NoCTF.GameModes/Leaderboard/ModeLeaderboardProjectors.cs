using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using System.Text.Json;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Koh.Configuration;

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

    public static IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted).ToDictionary(challenge => challenge.Id);
        var defaults = ParseCompetition(input.CompetitionConfigurationJson);
        var solves = input.Submissions
            .Where(fact => validTeams.ContainsKey(fact.TeamId)
                          && fact.Kind == SubmissionKind.Flag
                          && fact.Event is { IsDeleted: false, Result: ScoringResult.Correct }
                          && fact.ChallengeId is not null
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.ChallengeId.Value)))
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();

        var awarded = new Dictionary<Guid, List<(LeaderboardSubmissionFact Fact, long Points)>>();
        var solveNumber = new Dictionary<Guid, int>();
        foreach (var solve in solves)
        {
            var challengeId = solve.ChallengeId!.Value;
            var configuration = ParseChallenge(challenges.TryGetValue(challengeId, out var challenge)
                ? challenge.ConfigurationJson
                : null);
            var points = configuration.Points ?? defaults.DefaultPoints;
            var index = solveNumber.GetValueOrDefault(challengeId);
            var score = Calculate(points, index);
            if (index == 0)
                score += FirstBlood(configuration.BloodRewards ?? defaults.BloodRewards, points);
            solveNumber[challengeId] = index + 1;
            if (!awarded.TryGetValue(solve.TeamId, out var teamSolves))
                awarded[solve.TeamId] = teamSolves = [];
            teamSolves.Add((solve, score));
        }

        var rows = validTeams.Values.Select(team =>
        {
            var own = awarded.GetValueOrDefault(team.Id) ?? [];
            var summaries = own.GroupBy(item => item.Fact.ChallengeId!.Value)
                .Select(group => new LeaderboardChallengeSummary(
                    group.Key,
                    challenges.TryGetValue(group.Key, out var challenge) ? challenge.Direction : string.Empty,
                    group.Count()))
                .OrderBy(summary => summary.ChallengeId)
                .ToList();
            var last = own.Select(item => item.Fact.Event.OccurredAt).OrderByDescending(value => value).FirstOrDefault();
            return new LeaderboardEntry(0, team.Id, team.Name, own.Sum(item => item.Points), own.Count,
                last == default ? null : last, summaries);
        });
        return rows
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.LastScoreAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private static long Calculate(CtfPointConfiguration points, int index)
    {
        var value = points.InitialPoints * Math.Pow((double)points.DecayFactor, index);
        return Math.Max(points.MinimumPoints, (long)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static long FirstBlood(IReadOnlyList<BloodReward> rewards, CtfPointConfiguration points) =>
        rewards.Sum(reward => reward.Policy switch
        {
            BloodRewardPolicy.FixedPoints => (long)Math.Round(reward.Value, MidpointRounding.AwayFromZero),
            BloodRewardPolicy.InitialPointsPercentage => (long)Math.Round(points.InitialPoints * reward.Value / 100m, MidpointRounding.AwayFromZero),
            BloodRewardPolicy.SolveTimePointsPercentage => (long)Math.Round(points.InitialPoints * reward.Value / 100m, MidpointRounding.AwayFromZero),
            _ => 0
        });

    private static CtfConfiguration ParseCompetition(string? json) =>
        TryParse<CtfConfiguration>(json) ?? new(CtfConfiguration.CurrentSchemaVersion, new(500, 100, 10), []);

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
        if (string.IsNullOrWhiteSpace(input.CompetitionConfigurationJson))
            return ModeLeaderboardProjection.Project(input, submission => submission.Kind == SubmissionKind.Flag, includeSystemFacts: true);
        var configuration = TryParse(input.CompetitionConfigurationJson)
            ?? new AwdConfiguration(1, 300, 10, 2, 50, 100, 50, 50);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var values = teams.Keys.ToDictionary(team => team, _ => 0L);
        var solves = input.Submissions
            .Where(fact => teams.ContainsKey(fact.TeamId)
                          && fact.Kind == SubmissionKind.Flag
                          && fact.Event is { IsDeleted: false, Result: ScoringResult.Correct })
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();
        foreach (var solve in solves)
        {
            values[solve.TeamId] += configuration.AttackPoints;
            if (solve.VictimTeamId is { } victim
                && victim != solve.TeamId
                && values.ContainsKey(victim))
                values[victim] -= configuration.VictimPenalty;
        }
        foreach (var system in input.SystemEvents.Where(fact => !fact.Event.IsDeleted && fact.Event.Kind == ScoringEventKind.AwdServiceCheck && fact.Event.TeamId is not null))
        {
            var team = system.Event.TeamId!.Value;
            if (!values.ContainsKey(team)) continue;
            values[team] += system.Event.Result == ScoringResult.Correct
                ? configuration.ServiceOnlinePoints
                : -configuration.ServiceDownPenalty;
        }
        var rows = teams.Values.Select(team => new LeaderboardEntry(
            0,
            team.Id,
            team.Name,
            values[team.Id],
            solves.Count(solve => solve.TeamId == team.Id),
            LastSolve(solves, team.Id),
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
        try { return JsonSerializer.Deserialize<AwdConfiguration>(json!, JsonOptions); }
        catch (JsonException) { return null; }
    }
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
        if (string.IsNullOrWhiteSpace(input.CompetitionConfigurationJson))
            return ModeLeaderboardProjection.Project(input, submission => submission.Kind is SubmissionKind.Flag or SubmissionKind.Fix, includeSystemFacts: true);
        var competition = ParseCompetition(input.CompetitionConfigurationJson);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted).ToDictionary(challenge => challenge.Id);
        var facts = input.Submissions
            .Where(fact => teams.ContainsKey(fact.TeamId)
                          && fact.Kind is SubmissionKind.Flag or SubmissionKind.Fix
                          && fact.Event is { IsDeleted: false, Result: ScoringResult.Correct }
                          && fact.ChallengeId is not null
                          && (challenges.Count == 0 || challenges.ContainsKey(fact.ChallengeId.Value)))
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();
        var awarded = new Dictionary<Guid, List<(LeaderboardSubmissionFact Fact, long Points)>>();
        var milestones = new HashSet<(Guid TeamId, Guid ChallengeId, SubmissionKind Kind, int Round)>();
        foreach (var fact in facts)
        {
            var challengeId = fact.ChallengeId!.Value;
            var challenge = ParseChallenge(challenges.GetValueOrDefault(challengeId)?.ConfigurationJson);
            var achievement = fact.Kind == SubmissionKind.Flag
                ? challenge.Break ?? competition.Break
                : challenge.Fix ?? competition.Fix;
            var round = achievement.Settlement == AchievementSettlement.Milestone
                ? 0
                : Round(fact.ReceivedAt, input.CompetitionStartTime, competition.RoundDurationSeconds);
            if (!milestones.Add((fact.TeamId, challengeId, fact.Kind, round)))
                continue;
            if (!awarded.TryGetValue(fact.TeamId, out var teamFacts))
                awarded[fact.TeamId] = teamFacts = [];
            teamFacts.Add((fact, achievement.Points));
        }
        var rows = teams.Values.Select(team =>
        {
            var own = awarded.GetValueOrDefault(team.Id) ?? [];
            var last = own.Select(item => item.Fact.Event.OccurredAt).OrderByDescending(value => value).FirstOrDefault();
            return new LeaderboardEntry(0, team.Id, team.Name, own.Sum(item => item.Points), own.Count,
                last == default ? null : last, []);
        });
        return rows.OrderByDescending(row => row.Score)
            .ThenBy(row => row.LastScoreAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
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
        ?? new(1, 300, new(AchievementSettlement.PerRound, 50), new(AchievementSettlement.PerRound, 50));

    private static AwdpChallengeConfiguration ParseChallenge(string? json) =>
        TryParse<AwdpChallengeConfiguration>(json)
        ?? new(1, null, null, true, 10, 10);

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
        if (string.IsNullOrWhiteSpace(input.CompetitionConfigurationJson))
            return ModeLeaderboardProjection.Project(input, _ => false, includeSystemFacts: true);
        var configuration = Parse(input.CompetitionConfigurationJson);
        var teams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted).ToDictionary(team => team.Id);
        var observations = input.SystemEvents
            .Where(fact => fact.Event is
            {
                IsDeleted: false,
                Kind: ScoringEventKind.KohObservation,
                Result: ScoringResult.Correct,
                TeamId: not null
            } && teams.ContainsKey(fact.Event.TeamId!.Value))
            .OrderBy(fact => fact.Event.OccurredAt)
            .ThenBy(fact => fact.Event.Id)
            .ToList();
        var rows = teams.Values.Select(team =>
        {
            var own = observations.Where(fact => fact.Event.TeamId == team.Id).ToList();
            var last = own.Select(fact => fact.Event.OccurredAt).LastOrDefault();
            return new LeaderboardEntry(0, team.Id, team.Name,
                own.Count * configuration.ControlPointsPerInterval,
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

    private static KohConfiguration Parse(string json)
    {
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

public sealed class PenetrationLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Penetration;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        ModeLeaderboardProjection.Project(
            input,
            submission => submission.Kind == SubmissionKind.Flag,
            includeSystemFacts: true);
}

internal static class ModeLeaderboardProjection
{
    public static IReadOnlyList<LeaderboardEntry> Project(
        LeaderboardProjectionInput input,
        Func<LeaderboardSubmissionFact, bool> includeSubmission,
        bool includeSystemFacts)
    {
        var validTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .ToDictionary(team => team.Id);
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var submissions = input.Submissions
            .Where(fact => validTeams.ContainsKey(fact.TeamId)
                          && includeSubmission(fact)
                          && (challenges.Count == 0
                              || fact.ChallengeId is null
                              || challenges.ContainsKey(fact.ChallengeId.Value))
                          && !fact.Event.IsDeleted
                          && fact.Event.Result == ScoringResult.Correct)
            .OrderBy(fact => fact.ReceivedAt)
            .ThenBy(fact => fact.SubmissionId)
            .ToList();
        var system = includeSystemFacts
            ? input.SystemEvents
                .Where(fact => !fact.Event.IsDeleted
                               && fact.Event.Result == ScoringResult.Correct
                               && fact.Event.TeamId is not null
                               && validTeams.ContainsKey(fact.Event.TeamId.Value))
                .OrderBy(fact => fact.Event.OccurredAt)
                .ThenBy(fact => fact.Event.Id)
                .ToList()
            : [];

        var rows = validTeams.Values.Select(team =>
        {
            var ownSubmissions = submissions.Where(fact => fact.TeamId == team.Id).ToList();
            var ownSystem = system.Where(fact => fact.Event.TeamId == team.Id).ToList();
            var challengeSummaries = ownSubmissions
                .Where(fact => fact.ChallengeId.HasValue)
                .GroupBy(fact => fact.ChallengeId!.Value)
                .Select(group => new LeaderboardChallengeSummary(
                    group.Key,
                    challenges.TryGetValue(group.Key, out var challenge) ? challenge.Direction : string.Empty,
                    group.Count()))
                .OrderBy(summary => summary.ChallengeId)
                .ToList();
            var score = ownSubmissions.Count + ownSystem.Count;
            var last = ownSubmissions.Select(fact => fact.Event.OccurredAt)
                .Concat(ownSystem.Select(fact => fact.Event.OccurredAt))
                .OrderByDescending(value => value)
                .FirstOrDefault();
            return new LeaderboardEntry(
                0,
                team.Id,
                team.Name,
                score,
                ownSubmissions.Count,
                last == default ? null : last,
                challengeSummaries);
        });

        return rows
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.SolveCount)
            .ThenBy(row => row.LastScoreAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }
}
