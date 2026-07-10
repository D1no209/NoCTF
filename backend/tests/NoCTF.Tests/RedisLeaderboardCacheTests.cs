using NoCTF.Application.Leaderboard;
using StackExchange.Redis;

namespace NoCTF.Tests;

public class RedisLeaderboardCacheTests
{
    [Fact]
    public async Task VersionedUpdate_DoesNotAllowOlderSnapshotToOverwriteNewerSnapshot()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var cache = new RedisLeaderboardCache(redis);
        var competitionId = Guid.NewGuid();
        var oldVersion = await cache.ReserveUpdateVersionAsync(competitionId);
        var newVersion = await cache.ReserveUpdateVersionAsync(competitionId);
        var teamId = Guid.NewGuid();

        await cache.UpdateAsync(competitionId, [Entry(teamId, "new", 200)], newVersion);
        await cache.UpdateAsync(competitionId, [Entry(teamId, "old", 100)], oldVersion);

        var cached = Assert.Single((await cache.GetAsync(competitionId))!);
        Assert.Equal("new", cached.TeamName);
        Assert.Equal(200, cached.TotalScore);
        await cache.InvalidateAsync(competitionId);
    }

    private static LeaderboardEntry Entry(Guid teamId, string name, long score)
        => new(1, teamId, name, null, score, 1, DateTime.UtcNow);
}
