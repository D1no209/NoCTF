using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using System.Text.Json;
using NoCTF.GameModes.Ctf.Configuration;

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
        ModeLeaderboardProjection.Project(
            input,
            submission => submission.Kind == SubmissionKind.Flag,
            includeSystemFacts: true);
}

public sealed class AwdpLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Awdp;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        ModeLeaderboardProjection.Project(
            input,
            submission => submission.Kind is SubmissionKind.Flag or SubmissionKind.Fix,
            includeSystemFacts: true);
}

public sealed class KohLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Koh;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        ModeLeaderboardProjection.Project(input, _ => false, includeSystemFacts: true);
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
