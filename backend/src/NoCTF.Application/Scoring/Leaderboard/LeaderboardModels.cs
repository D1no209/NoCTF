namespace NoCTF.Application.Scoring.Leaderboard;

public enum LeaderboardBloodRank
{
    First = 1,
    Second = 2,
    Third = 3
}

/// <summary>
/// The sparse value of one team × competition-challenge cell.
/// An absent cell means that the team has no public result for that challenge.
/// </summary>
public sealed record LeaderboardCell(
    Guid CompetitionChallengeId,
    long Score,
    DateTimeOffset? SolvedAt,
    string? SolverName,
    LeaderboardBloodRank? BloodRank)
{
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
}

/// <summary>Projection-only cell before it is attached to a ranked team row.</summary>
public sealed record LeaderboardCellFact(
    Guid TeamId,
    Guid CompetitionChallengeId,
    long Score,
    DateTimeOffset? SolvedAt,
    string? SolverName)
{
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
}

public sealed record LeaderboardChallengeInfo(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    long? CurrentScore,
    long? CurrentBreakScore,
    long? CurrentFixScore);

public sealed record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    string TrackKey = "default")
{
    public long? AttackScore { get; init; }
    public long? DefenseScore { get; init; }
    public long? PenaltyScore { get; init; }

    /// <summary>Only solved/corrected cells are serialized; the matrix is sparse.</summary>
    public IReadOnlyList<LeaderboardCell> Cells { get; init; } = [];
}
