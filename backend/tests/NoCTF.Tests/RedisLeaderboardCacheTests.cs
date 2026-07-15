using NoCTF.Application.Leaderboard;
using StackExchange.Redis;
using System.Reflection;
using System.Text.Json;

namespace NoCTF.Tests;

public class RedisLeaderboardCacheTests
{
    [Fact]
    public void SortAndRank_MatchesLeaderboardServiceOrderingWithoutCompositeScores()
    {
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            new LeaderboardEntry(99, Guid.NewGuid(), "null-first-solve", null, 100, 1, null),
            new LeaderboardEntry(99, Guid.NewGuid(), "a", null, 100, 1, timestamp),
            new LeaderboardEntry(99, Guid.NewGuid(), "lower-score", null, 99, 99, timestamp.AddYears(-1)),
            new LeaderboardEntry(99, Guid.NewGuid(), "later-solve", null, 100, 1, timestamp.AddSeconds(1)),
            new LeaderboardEntry(99, Guid.NewGuid(), "Z", null, 100, 1, timestamp),
            new LeaderboardEntry(99, Guid.NewGuid(), "more-solves", null, 100, 2, null),
            new LeaderboardEntry(99, Guid.NewGuid(), "max-date", null, 100, 1, DateTime.MaxValue),
            new LeaderboardEntry(99, Guid.NewGuid(), "huge-score", null, long.MaxValue, 0, null)
        };

        var ordered = LeaderboardOrdering.SortAndRank(entries);

        Assert.Equal(
            ["huge-score", "more-solves", "Z", "a", "later-solve", "max-date", "null-first-solve", "lower-score"],
            ordered.Select(entry => entry.TeamName));
        Assert.Equal(Enumerable.Range(1, entries.Length), ordered.Select(entry => entry.Rank));
    }

    [Fact]
    public async Task GetAsync_UsesOneAtomicSnapshotRead()
    {
        var first = Entry(Guid.NewGuid(), "first", 200);
        var second = Entry(Guid.NewGuid(), "second", 100);
        var (redis, database) = RedisConnectionDispatchProxy.Create([first, second]);
        var cache = new RedisLeaderboardCache(redis);

        var result = await cache.GetAsync(Guid.NewGuid());

        Assert.Collection(
            result!,
            entry => Assert.Equal(first.TeamId, entry.TeamId),
            entry => Assert.Equal(second.TeamId, entry.TeamId));
        Assert.Equal(1, database.AtomicSnapshotReadCount);
        Assert.Equal(0, database.MultiFieldHashReadCount);
        Assert.Equal(0, database.SingleFieldHashReadCount);
        Assert.Equal([first.TeamId.ToString(), second.TeamId.ToString()], database.RequestedHashFields);
    }

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

    [Fact]
    public async Task VersionedUpdate_PreservesExactMultiFieldOrdering()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var cache = new RedisLeaderboardCache(redis);
        var competitionId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            new LeaderboardEntry(0, Guid.NewGuid(), "null", null, long.MaxValue, 1, null),
            new LeaderboardEntry(0, Guid.NewGuid(), "later", null, long.MaxValue, 1, timestamp.AddTicks(1)),
            new LeaderboardEntry(0, Guid.NewGuid(), "more-solves", null, long.MaxValue, 2, null),
            new LeaderboardEntry(0, Guid.NewGuid(), "earlier", null, long.MaxValue, 1, timestamp)
        };

        await cache.UpdateAsync(competitionId, entries);

        var cached = (await cache.GetAsync(competitionId))!;
        Assert.Equal(
            ["more-solves", "earlier", "later", "null"],
            cached.Select(entry => entry.TeamName));
        await cache.InvalidateAsync(competitionId);
    }

    [Fact]
    public async Task InvalidateAsync_AdvancesVersionSoInFlightSnapshotCannotRepopulateCache()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var cache = new RedisLeaderboardCache(redis);
        var competitionId = Guid.NewGuid();
        var staleVersion = await cache.ReserveUpdateVersionAsync(competitionId);
        var teamId = Guid.NewGuid();

        await cache.InvalidateAsync(competitionId);
        await cache.UpdateAsync(competitionId, [Entry(teamId, "stale", 100)], staleVersion);

        Assert.Null(await cache.GetAsync(competitionId));

        var freshVersion = await cache.ReserveUpdateVersionAsync(competitionId);
        await cache.UpdateAsync(competitionId, [Entry(teamId, "fresh", 200)], freshVersion);
        Assert.Equal("fresh", Assert.Single((await cache.GetAsync(competitionId))!).TeamName);
        await cache.InvalidateAsync(competitionId);
    }

    [Fact]
    public async Task EmptySnapshot_IsCachedUntilInvalidated()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var cache = new RedisLeaderboardCache(redis);
        var competitionId = Guid.NewGuid();

        await cache.UpdateAsync(competitionId, []);

        Assert.Empty((await cache.GetAsync(competitionId))!);

        await cache.InvalidateAsync(competitionId);
        Assert.Null(await cache.GetAsync(competitionId));
    }

    private static LeaderboardEntry Entry(Guid teamId, string name, long score)
        => new(1, teamId, name, null, score, 1, DateTime.UtcNow);
}

file class RedisConnectionDispatchProxy : DispatchProxy
{
    private IDatabase _database = null!;

    public static (IConnectionMultiplexer Connection, RedisDatabaseDispatchProxy Database) Create(
        IReadOnlyList<LeaderboardEntry> entries)
    {
        var connection = DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionDispatchProxy>();
        var connectionProxy = (RedisConnectionDispatchProxy)(object)connection;
        var database = RedisDatabaseDispatchProxy.Create(entries);
        connectionProxy._database = database.Database;
        return (connection, database);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase)
            ? _database
            : throw new NotSupportedException(targetMethod?.Name);
}

file class RedisDatabaseDispatchProxy : DispatchProxy
{
    private RedisValue[] _teamIds = [];
    private Dictionary<string, RedisValue> _entries = [];

    public IDatabase Database { get; private set; } = null!;
    public int MultiFieldHashReadCount { get; private set; }
    public int SingleFieldHashReadCount { get; private set; }
    public int AtomicSnapshotReadCount { get; private set; }
    public string[] RequestedHashFields { get; private set; } = [];

    public static RedisDatabaseDispatchProxy Create(IReadOnlyList<LeaderboardEntry> entries)
    {
        var database = DispatchProxy.Create<IDatabase, RedisDatabaseDispatchProxy>();
        var proxy = (RedisDatabaseDispatchProxy)(object)database;
        proxy.Database = database;
        proxy._teamIds = entries.Select(entry => (RedisValue)entry.TeamId.ToString()).ToArray();
        proxy._entries = entries.ToDictionary(
            entry => entry.TeamId.ToString(),
            entry => (RedisValue)JsonSerializer.Serialize(entry),
            StringComparer.Ordinal);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IDatabaseAsync.ScriptEvaluateAsync) &&
            targetMethod.ReturnType == typeof(Task<RedisResult>))
        {
            AtomicSnapshotReadCount++;
            var values = _teamIds
                .SelectMany(teamId => new[] { teamId, _entries[teamId.ToString()] })
                .ToArray();
            RequestedHashFields = _teamIds.Select(teamId => teamId.ToString()).ToArray();
            return Task.FromResult(RedisResult.Create(values));
        }

        if (targetMethod?.Name == nameof(IDatabaseAsync.SortedSetRangeByRankAsync))
            return Task.FromResult(_teamIds);

        if (targetMethod?.Name == nameof(IDatabaseAsync.HashGetAsync) &&
            targetMethod.ReturnType == typeof(Task<RedisValue[]>))
        {
            MultiFieldHashReadCount++;
            var fields = (RedisValue[])args![1]!;
            RequestedHashFields = fields.Select(field => field.ToString()).ToArray();
            return Task.FromResult(fields
                .Select(field => _entries.GetValueOrDefault(field.ToString(), RedisValue.Null))
                .ToArray());
        }

        if (targetMethod?.Name == nameof(IDatabaseAsync.HashGetAsync) &&
            targetMethod.ReturnType == typeof(Task<RedisValue>))
        {
            SingleFieldHashReadCount++;
            return Task.FromResult(_entries.GetValueOrDefault(args![1]!.ToString()!, RedisValue.Null));
        }

        throw new NotSupportedException(targetMethod?.Name);
    }
}
