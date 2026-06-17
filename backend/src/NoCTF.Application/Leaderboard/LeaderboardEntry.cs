namespace NoCTF.Application.Leaderboard;

/// <summary>A single row in the competition leaderboard.</summary>
public record LeaderboardEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    long TotalScore,
    int SolvedCount,
    DateTime? FirstSolveAt);
