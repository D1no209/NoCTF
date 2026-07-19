using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Leaderboard;

public sealed class CtfLeaderboardProjector : IGameModeLeaderboardProjector
{
    public GameMode Mode => GameMode.Ctf;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input) =>
        ModeLeaderboardProjection.Project(
            input,
            submission => submission.Kind == SubmissionKind.Flag,
            includeSystemFacts: false);
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
        var submissions = input.Submissions
            .Where(fact => validTeams.ContainsKey(fact.TeamId)
                          && includeSubmission(fact)
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
                .Select(group => new LeaderboardChallengeSummary(group.Key, string.Empty, group.Count()))
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
