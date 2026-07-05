using NoCTF.Application.Leaderboard;

namespace NoCTF.Tests;

internal sealed class NullRedisLeaderboardCache : IRedisLeaderboardCache
{
    public Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<LeaderboardEntry>?> GetAsync(Guid competitionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LeaderboardEntry>?>(null);

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct = default)
        => Task.CompletedTask;
}
