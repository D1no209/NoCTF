namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardResponse(Guid CompetitionId, DateTimeOffset GeneratedAt, IReadOnlyList<LeaderboardEntry> Entries);
public sealed record LeaderboardProcessingResponse(Guid CompetitionId, string State);

public interface ILeaderboardCache
{
    Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken);
    Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken);
}
