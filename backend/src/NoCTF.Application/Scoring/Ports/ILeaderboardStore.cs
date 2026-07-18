using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Scoring.Ports;

public interface ILeaderboardStore
{
    Task<LeaderboardSnapshot?> GetAuthoritativeAsync(Guid competitionId, CancellationToken cancellationToken);
    Task PublishCacheAsync(LeaderboardSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetDirtyCompetitionsAsync(CancellationToken cancellationToken);
    Task MarkCleanAsync(Guid competitionId, long projectionVersion, CancellationToken cancellationToken);
}
