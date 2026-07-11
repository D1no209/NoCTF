using StackExchange.Redis;
using System.Text.Json;

namespace NoCTF.Application.Leaderboard;

public interface IRedisLeaderboardCache
{
    Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, CancellationToken ct = default);
    Task<long> ReserveUpdateVersionAsync(Guid competitionId, CancellationToken ct = default)
        => Task.FromResult(DateTime.UtcNow.Ticks);
    Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, long version, CancellationToken ct = default)
        => UpdateAsync(competitionId, entries, ct);
    Task<IReadOnlyList<LeaderboardEntry>?> GetAsync(Guid competitionId, CancellationToken ct = default);
    Task InvalidateAsync(Guid competitionId, CancellationToken ct = default);
}

/// <summary>
/// Redis Sorted Set cache for leaderboard data.
/// Key pattern: noctf:leaderboard:{competitionId}
/// Score encoding: totalPoints * 1_000_000 + tie-breaker offset (so ZREVRANGE gives correct order).
/// Tie-breaker: earlier first-solve = higher rank when scores are equal.
/// We store the full entry as JSON in a companion hash: noctf:leaderboard:data:{competitionId}
/// </summary>
public class RedisLeaderboardCache(IConnectionMultiplexer redis) : IRedisLeaderboardCache
{
    // Epoch used to compute tie-breaker offset: offset = (maxEpoch - firstSolveEpoch) / maxEpoch * 999999
    // Ensures earlier first-solve gets a slightly higher composite score.
    private static readonly long MaxEpochSeconds = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static string SortedSetKey(Guid competitionId) => $"noctf:leaderboard:{competitionId}";
    private static string DataHashKey(Guid competitionId) => $"noctf:leaderboard:data:{competitionId}";
    private static string VersionCounterKey(Guid competitionId) => $"noctf:leaderboard:version-counter:{competitionId}";
    private static string AppliedVersionKey(Guid competitionId) => $"noctf:leaderboard:applied-version:{competitionId}";

    private const string AtomicReplaceScript = """
        local current = tonumber(redis.call('GET', KEYS[3]) or '0')
        local incoming = tonumber(ARGV[1])
        if incoming <= current then
            return 0
        end
        redis.call('DEL', KEYS[1], KEYS[2])
        local index = 2
        while index <= #ARGV do
            redis.call('ZADD', KEYS[1], ARGV[index + 1], ARGV[index])
            redis.call('HSET', KEYS[2], ARGV[index], ARGV[index + 2])
            index = index + 3
        end
        redis.call('SET', KEYS[3], incoming)
        return 1
        """;

    private const string AtomicInvalidateScript = """
        local version = redis.call('INCR', KEYS[3])
        redis.call('DEL', KEYS[1], KEYS[2])
        redis.call('SET', KEYS[4], version)
        return version
        """;

    public async Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, CancellationToken ct = default)
    {
        var version = await ReserveUpdateVersionAsync(competitionId, ct);
        await UpdateAsync(competitionId, entries, version, ct);
    }

    public async Task<long> ReserveUpdateVersionAsync(Guid competitionId, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringIncrementAsync(VersionCounterKey(competitionId));
        return value;
    }

    public async Task UpdateAsync(
        Guid competitionId,
        IReadOnlyList<LeaderboardEntry> entries,
        long version,
        CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var ssKey = SortedSetKey(competitionId);
        var dataKey = DataHashKey(competitionId);
        var values = new RedisValue[1 + entries.Count * 3];
        values[0] = version;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var tieBreakerOffset = ComputeTieBreakerOffset(entry.FirstSolveAt);
            // Composite score: higher is better. totalScore * 1_000_000 + tie-breaker
            double compositeScore = entry.TotalScore * 1_000_000.0 + tieBreakerOffset;

            var offset = 1 + i * 3;
            values[offset] = entry.TeamId.ToString();
            values[offset + 1] = compositeScore;
            values[offset + 2] = JsonSerializer.Serialize(entry);
        }
        await db.ScriptEvaluateAsync(
            AtomicReplaceScript,
            [ssKey, dataKey, AppliedVersionKey(competitionId)],
            values);
    }

    public async Task<IReadOnlyList<LeaderboardEntry>?> GetAsync(Guid competitionId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var ssKey = SortedSetKey(competitionId);
        var dataKey = DataHashKey(competitionId);

        // ZREVRANGE returns team IDs ordered by score descending
        var teamIds = await db.SortedSetRangeByRankAsync(ssKey, 0, -1, Order.Descending);
        if (teamIds.Length == 0)
            return null;

        var result = new List<LeaderboardEntry>(teamIds.Length);
        for (int i = 0; i < teamIds.Length; i++)
        {
            var json = await db.HashGetAsync(dataKey, teamIds[i]);
            if (json.IsNullOrEmpty)
                continue;

            var entry = JsonSerializer.Deserialize<LeaderboardEntry>(json!);
            if (entry is not null)
                // Re-assign rank based on sorted position
                result.Add(entry with { Rank = i + 1 });
        }

        return result.Count > 0 ? result : null;
    }

    public async Task InvalidateAsync(Guid competitionId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.ScriptEvaluateAsync(
            AtomicInvalidateScript,
            [
                SortedSetKey(competitionId),
                DataHashKey(competitionId),
                VersionCounterKey(competitionId),
                AppliedVersionKey(competitionId)
            ]);
    }

    private static double ComputeTieBreakerOffset(DateTime? firstSolveAt)
    {
        if (firstSolveAt is null)
            return 0;

        var epochSeconds = new DateTimeOffset(firstSolveAt.Value, TimeSpan.Zero).ToUnixTimeSeconds();
        // Earlier solve → larger offset (better rank)
        var offset = (double)(MaxEpochSeconds - epochSeconds) / MaxEpochSeconds * 999_999.0;
        return Math.Max(0, Math.Min(999_999, offset));
    }
}
