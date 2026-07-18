namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardChallengeSummary(Guid ChallengeId, string Direction, int SolveCount);

public sealed record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardChallengeSummary> Challenges);

public sealed record LeaderboardSnapshot(
    Guid CompetitionId,
    long ProjectionVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LeaderboardEntry> Entries);
