using StackExchange.Redis;
using System.Text.Json;

namespace NoCTF.Application.Leaderboard;

public interface IRedisLeaderboardCache
{
    Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, CancellationToken ct = default);
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

    public async Task UpdateAsync(Guid competitionId, IReadOnlyList<LeaderboardEntry> entries, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var ssKey = SortedSetKey(competitionId);
        var dataKey = DataHashKey(competitionId);

        var sortedSetEntries = new SortedSetEntry[entries.Count];
        var hashEntries = new HashEntry[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var tieBreakerOffset = ComputeTieBreakerOffset(entry.FirstSolveAt);
            // Composite score: higher is better. totalScore * 1_000_000 + tie-breaker
            double compositeScore = entry.TotalScore * 1_000_000.0 + tieBreakerOffset;

            sortedSetEntries[i] = new SortedSetEntry(entry.TeamId.ToString(), compositeScore);
            hashEntries[i] = new HashEntry(entry.TeamId.ToString(), JsonSerializer.Serialize(entry));
        }

        var batch = db.CreateBatch();
        var ssTask = batch.SortedSetAddAsync(ssKey, sortedSetEntries, CommandFlags.None);
        var hashTask = batch.HashSetAsync(dataKey, hashEntries);
        batch.Execute();
        await Task.WhenAll(ssTask, hashTask);
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
        await db.KeyDeleteAsync([SortedSetKey(competitionId), DataHashKey(competitionId)]);
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
