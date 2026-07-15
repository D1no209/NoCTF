using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using StackExchange.Redis;

namespace NoCTF.API.Auth;

public interface IUserTokenVersionCache
{
    Task<int?> GetAsync(Guid userId, ApplicationDbContext db, CancellationToken ct);
    Task SetAsync(Guid userId, int tokenVersion);
}

/// <summary>
/// Keeps JWT revocation checks off PostgreSQL's request hot path while using
/// Redis publication to invalidate the small per-API-instance cache.
/// </summary>
public sealed class RedisUserTokenVersionCache : IUserTokenVersionCache
{
    private static readonly TimeSpan LocalTtl = TimeSpan.FromSeconds(15);
    private const int LocalCapacity = 10_000;
    private const int LocalTrimTarget = 9_000;

    private readonly ConcurrentDictionary<Guid, CacheEntry> _local = new();
    private readonly IUserTokenVersionDistributedStore _store;
    private readonly ILogger<RedisUserTokenVersionCache> _logger;
    private readonly TimeProvider _timeProvider;
    private int _trimInProgress;

    public RedisUserTokenVersionCache(
        IConnectionMultiplexer redis,
        ILogger<RedisUserTokenVersionCache> logger)
        : this(new RedisUserTokenVersionStore(redis), logger, TimeProvider.System)
    {
    }

    internal RedisUserTokenVersionCache(
        IUserTokenVersionDistributedStore store,
        ILogger<RedisUserTokenVersionCache> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _logger = logger;
        _timeProvider = timeProvider;
        store.Subscribe(ApplyInvalidation);
    }

    public async Task<int?> GetAsync(Guid userId, ApplicationDbContext db, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        if (_local.TryGetValue(userId, out var cached) && cached.ExpiresAt > now)
            return cached.TokenVersion;
        if (cached is not null)
            ((ICollection<KeyValuePair<Guid, CacheEntry>>)_local).Remove(
                new KeyValuePair<Guid, CacheEntry>(userId, cached));

        // PostgreSQL remains authoritative on every local-cache miss. Redis is
        // used only to fan out monotonic invalidations, so a stale distributed
        // value can never resurrect a revoked token after an outage.
        var version = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (int?)user.TokenVersion)
            .FirstOrDefaultAsync(ct);
        if (!version.HasValue)
        {
            _local.TryRemove(userId, out _);
            await PublishBestEffortAsync(userId, int.MaxValue);
            _local.TryRemove(userId, out _);
            return null;
        }

        var authoritativeVersion = SetLocal(userId, version.Value);
        return await PublishBestEffortAsync(userId, authoritativeVersion);
    }

    public async Task SetAsync(Guid userId, int tokenVersion)
    {
        var authoritativeVersion = SetLocal(userId, tokenVersion);
        await PublishBestEffortAsync(userId, authoritativeVersion);
    }

    private async Task<int> PublishBestEffortAsync(Guid userId, int tokenVersion)
    {
        try
        {
            var appliedVersion = await _store.SetAtLeastAndPublishAsync(userId, tokenVersion);
            return SetLocal(userId, appliedVersion);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(
                ex,
                "Redis token-version update failed; PostgreSQL and the authoritative local version will be used.");
            return tokenVersion;
        }
    }

    private void ApplyInvalidation(Guid userId, int tokenVersion)
        => SetLocal(userId, tokenVersion);

    private int SetLocal(Guid userId, int tokenVersion)
    {
        var expiresAt = _timeProvider.GetUtcNow().Add(LocalTtl);
        var entry = _local.AddOrUpdate(
            userId,
            new CacheEntry(tokenVersion, expiresAt),
            (_, current) => tokenVersion >= current.TokenVersion
                ? new CacheEntry(tokenVersion, expiresAt)
                : current);
        TrimLocalCacheIfNeeded();
        return entry.TokenVersion;
    }

    private void TrimLocalCacheIfNeeded()
    {
        if (_local.Count <= LocalCapacity || Interlocked.Exchange(ref _trimInProgress, 1) != 0)
            return;

        try
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var pair in _local.Where(pair => pair.Value.ExpiresAt <= now))
                ((ICollection<KeyValuePair<Guid, CacheEntry>>)_local).Remove(pair);

            var removeCount = Math.Max(0, _local.Count - LocalTrimTarget);
            foreach (var pair in _local.OrderBy(pair => pair.Value.ExpiresAt).Take(removeCount))
                ((ICollection<KeyValuePair<Guid, CacheEntry>>)_local).Remove(pair);
        }
        finally
        {
            Volatile.Write(ref _trimInProgress, 0);
        }
    }

    private sealed record CacheEntry(int TokenVersion, DateTimeOffset ExpiresAt);
}

internal interface IUserTokenVersionDistributedStore
{
    void Subscribe(Action<Guid, int> handler);
    Task<int?> GetAsync(Guid userId, CancellationToken ct);
    Task<int> SetAtLeastAndPublishAsync(Guid userId, int tokenVersion);
}

internal sealed class RedisUserTokenVersionStore(IConnectionMultiplexer redis)
    : IUserTokenVersionDistributedStore
{
    private const string KeyPrefix = "noctf:auth:token-version:";
    private const string InvalidationChannelName = "noctf:auth:token-version:changed";
    private static readonly RedisChannel InvalidationChannel =
        RedisChannel.Literal(InvalidationChannelName);
    private static readonly TimeSpan RedisTtl = TimeSpan.FromMinutes(10);

    private const string AtomicSetAtLeastAndPublishScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '-1') or -1
        local incoming = tonumber(ARGV[1])
        local applied = current
        if incoming > current then
            redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
            applied = incoming
        else
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        redis.call('PUBLISH', ARGV[3], ARGV[4] .. ':' .. tostring(applied))
        return applied
        """;

    public void Subscribe(Action<Guid, int> handler)
        => redis.GetSubscriber().Subscribe(InvalidationChannel, (_, payload) =>
        {
            var value = payload.ToString();
            var separator = value.LastIndexOf(':');
            if (separator <= 0 ||
                !Guid.TryParse(value[..separator], out var userId) ||
                !int.TryParse(
                    value[(separator + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var version))
            {
                return;
            }

            handler(userId, version);
        });

    public async Task<int?> GetAsync(Guid userId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(userId)).WaitAsync(ct);
        return value.HasValue &&
               int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            ? version
            : null;
    }

    public async Task<int> SetAtLeastAndPublishAsync(Guid userId, int tokenVersion)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            AtomicSetAtLeastAndPublishScript,
            [Key(userId)],
            [
                tokenVersion,
                (long)RedisTtl.TotalMilliseconds,
                InvalidationChannelName,
                userId.ToString("D")
            ]);
        return checked((int)(long)result);
    }

    private static string Key(Guid userId) => $"{KeyPrefix}{userId:N}";
}
