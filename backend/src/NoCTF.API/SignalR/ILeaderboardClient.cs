namespace NoCTF.API.SignalR;

/// <summary>
/// Strongly-typed client interface for leaderboard real-time updates.
/// </summary>
public interface ILeaderboardClient
{
    /// <summary>Sent when a team's score changes.</summary>
    Task ReceiveScoreUpdate(ScoreUpdateDto update);

    /// <summary>Sent when the full leaderboard snapshot is pushed (e.g., on connect).</summary>
    Task ReceiveLeaderboardSnapshot(IEnumerable<LeaderboardEntryDto> entries);
}

public record ScoreUpdateDto(
    Guid TeamId,
    string TeamName,
    long NewScore,
    int NewRank,
    DateTimeOffset Timestamp);

public record LeaderboardEntryDto(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolvedCount);
