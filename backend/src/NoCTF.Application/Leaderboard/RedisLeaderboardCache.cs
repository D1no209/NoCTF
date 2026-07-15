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
/// The sorted-set score is the exact, precomputed rank position rather than a
/// lossy composite of the leaderboard fields.
/// We store the full entry as JSON in a companion hash: noctf:leaderboard:data:{competitionId}
/// </summary>
public class RedisLeaderboardCache(IConnectionMultiplexer redis) : IRedisLeaderboardCache
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VersionLifetime = TimeSpan.FromDays(7);
    private static string SortedSetKey(Guid competitionId) => $"noctf:leaderboard:{competitionId}";
    private static string DataHashKey(Guid competitionId) => $"noctf:leaderboard:data:{competitionId}";
    private static string VersionCounterKey(Guid competitionId) => $"noctf:leaderboard:version-counter:{competitionId}";
    private static string AppliedVersionKey(Guid competitionId) => $"noctf:leaderboard:applied-version:{competitionId}";
    private static string SnapshotMarkerKey(Guid competitionId) => $"noctf:leaderboard:snapshot:{competitionId}";

    private const string AtomicReplaceScript = """
        local current = tonumber(redis.call('GET', KEYS[3]) or '0')
        local incoming = tonumber(ARGV[1])
        if incoming <= current then
            return 0
        end
        redis.call('DEL', KEYS[1], KEYS[2])
        local index = 4
        while index <= #ARGV do
            redis.call('ZADD', KEYS[1], ARGV[index + 1], ARGV[index])
            redis.call('HSET', KEYS[2], ARGV[index], ARGV[index + 2])
            index = index + 3
        end
        redis.call('SET', KEYS[3], incoming)
        redis.call('SET', KEYS[5], '1', 'PX', ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        redis.call('PEXPIRE', KEYS[2], ARGV[2])
        redis.call('PEXPIRE', KEYS[3], ARGV[3])
        redis.call('PEXPIRE', KEYS[4], ARGV[3])
        return 1
        """;

    private const string ReserveVersionScript = """
        local version = redis.call('INCR', KEYS[1])
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return version
        """;

    private const string AtomicInvalidateScript = """
        local version = redis.call('INCR', KEYS[3])
        redis.call('DEL', KEYS[1], KEYS[2], KEYS[5])
        redis.call('SET', KEYS[4], version)
        redis.call('PEXPIRE', KEYS[3], ARGV[1])
        redis.call('PEXPIRE', KEYS[4], ARGV[1])
        return version
        """;

    private const string AtomicReadScript = """
        local ids = redis.call('ZREVRANGE', KEYS[1], 0, -1)
        if #ids == 0 then
            if redis.call('EXISTS', KEYS[3]) == 1 then
                return {'__noctf_empty_snapshot__'}
            end
            return {}
        end
        local result = {}
        for _, id in ipairs(ids) do
            local value = redis.call('HGET', KEYS[2], id)
            if not value then
                return {}
            end
            table.insert(result, id)
            table.insert(result, value)
        end
        return result
        """;

    public async Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, CancellationToken ct = default)
    {
        var version = await ReserveUpdateVersionAsync(competitionId, ct);
        await UpdateAsync(competitionId, entries, version, ct);
    }

    public async Task<long> ReserveUpdateVersionAsync(Guid competitionId, CancellationToken ct = default)
    {
        var result = await redis.GetDatabase()
            .ScriptEvaluateAsync(
                ReserveVersionScript,
                [VersionCounterKey(competitionId)],
                [(long)VersionLifetime.TotalMilliseconds])
            .WaitAsync(ct);
        return (long)result;
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
        var orderedEntries = LeaderboardOrdering.SortAndRank(entries);
        var values = new RedisValue[3 + orderedEntries.Count * 3];
        values[0] = version;
        values[1] = (long)SnapshotLifetime.TotalMilliseconds;
        values[2] = (long)VersionLifetime.TotalMilliseconds;

        for (int i = 0; i < orderedEntries.Count; i++)
        {
            var entry = orderedEntries[i];

            var offset = 3 + i * 3;
            values[offset] = entry.TeamId.ToString();
            // ZREVRANGE returns the largest score first. Every team receives a
            // distinct integer position, which is exactly representable as a
            // Redis double for any realistic leaderboard size.
            values[offset + 1] = orderedEntries.Count - i;
            values[offset + 2] = JsonSerializer.Serialize(entry);
        }
        await db.ScriptEvaluateAsync(
                AtomicReplaceScript,
                [
                    ssKey,
                    dataKey,
                    AppliedVersionKey(competitionId),
                    VersionCounterKey(competitionId),
                    SnapshotMarkerKey(competitionId)
                ],
                values)
            .WaitAsync(ct);
    }

    public async Task<IReadOnlyList<LeaderboardEntry>?> GetAsync(Guid competitionId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var ssKey = SortedSetKey(competitionId);
        var dataKey = DataHashKey(competitionId);

        // Read the sorted ids and their payloads in one Lua execution. Separate
        // ZRANGE/HMGET calls can otherwise straddle an atomic writer and combine
        // ids from one version with payloads from another.
        var raw = await db.ScriptEvaluateAsync(
                AtomicReadScript,
                [ssKey, dataKey, SnapshotMarkerKey(competitionId)])
            .WaitAsync(ct);
        var values = (RedisResult[]?)raw;
        if (values is { Length: 1 } &&
            string.Equals(
                (string?)values[0],
                "__noctf_empty_snapshot__",
                StringComparison.Ordinal))
        {
            return [];
        }
        if (values is null || values.Length == 0 || values.Length % 2 != 0)
            return null;

        var result = new List<LeaderboardEntry>(values.Length / 2);
        try
        {
            for (int i = 0; i < values.Length; i += 2)
            {
                var serialized = (string?)values[i + 1];
                if (string.IsNullOrWhiteSpace(serialized))
                    return null;
                var entry = JsonSerializer.Deserialize<LeaderboardEntry>(serialized);
                if (entry is null)
                    return null;

                // Re-assign rank based on sorted position.
                result.Add(entry with { Rank = i / 2 + 1 });
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return result;
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
                    AppliedVersionKey(competitionId),
                    SnapshotMarkerKey(competitionId)
                ],
                [(long)VersionLifetime.TotalMilliseconds])
            .WaitAsync(ct);
    }

}
