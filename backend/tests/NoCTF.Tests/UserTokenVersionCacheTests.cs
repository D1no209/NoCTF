using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.API.Auth;
using NoCTF.Core;
using NoCTF.Infrastructure;
using StackExchange.Redis;

namespace NoCTF.Tests;

public class UserTokenVersionCacheTests
{
    [Fact]
    public async Task FailedPublication_NeverFallsBackToOlderRedisVersion()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, tokenVersion: 2);
        var store = new TokenVersionStoreStub { Version = 1, FailWrites = true };
        var time = new ManualTimeProvider();
        var cache = CreateCache(store, time);

        await cache.SetAsync(userId, 2);
        time.Advance(TimeSpan.FromSeconds(16));

        Assert.Equal(2, await cache.GetAsync(userId, db, CancellationToken.None));
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(1, store.Version);
    }

    [Fact]
    public async Task PostgreSqlFallback_RepairsRedisAndRestoresFastPath()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, tokenVersion: 3);
        var store = new TokenVersionStoreStub { Version = 2, FailWrites = true };
        var time = new ManualTimeProvider();
        var cache = CreateCache(store, time);

        await cache.SetAsync(userId, 3);
        time.Advance(TimeSpan.FromSeconds(16));
        store.FailWrites = false;

        Assert.Equal(3, await cache.GetAsync(userId, db, CancellationToken.None));
        Assert.Equal(3, store.Version);
        Assert.Equal(0, store.ReadCount);

        time.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(3, await cache.GetAsync(userId, db, CancellationToken.None));
        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task OutOfOrderInvalidations_CannotRegressKnownVersion()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, tokenVersion: 5);
        var store = new TokenVersionStoreStub();
        var time = new ManualTimeProvider();
        var cache = CreateCache(store, time);

        store.Notify(userId, 5);
        store.Notify(userId, 4);

        Assert.Equal(5, await cache.GetAsync(userId, db, CancellationToken.None));
    }

    [Fact]
    public async Task RedisStore_AtomicPublicationCannotRegressVersion()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var userId = Guid.NewGuid();
        var store = new RedisUserTokenVersionStore(redis);
        store.Subscribe((_, _) => { });

        Assert.Equal(5, await store.SetAtLeastAndPublishAsync(userId, 5));
        Assert.Equal(5, await store.SetAtLeastAndPublishAsync(userId, 4));
        Assert.Equal(5, await store.GetAsync(userId, CancellationToken.None));

        await redis.GetDatabase().KeyDeleteAsync($"noctf:auth:token-version:{userId:N}");
    }

    private static RedisUserTokenVersionCache CreateCache(
        IUserTokenVersionDistributedStore store,
        TimeProvider timeProvider)
        => new(
            store,
            NullLogger<RedisUserTokenVersionCache>.Instance,
            timeProvider);

    private static ApplicationDbContext CreateDb(Guid userId, int tokenVersion)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options, new TokenCacheTenantContext());
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "user",
            Email = "user@example.test",
            PasswordHash = "hash",
            TokenVersion = tokenVersion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return db;
    }
}

file sealed class TokenVersionStoreStub : IUserTokenVersionDistributedStore
{
    private Action<Guid, int>? _handler;

    public int? Version { get; set; }
    public bool FailWrites { get; set; }
    public int ReadCount { get; private set; }

    public void Subscribe(Action<Guid, int> handler) => _handler = handler;

    public Task<int?> GetAsync(Guid userId, CancellationToken ct)
    {
        ReadCount++;
        return Task.FromResult(Version);
    }

    public Task<int> SetAtLeastAndPublishAsync(Guid userId, int tokenVersion)
    {
        if (FailWrites)
        {
            throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "Simulated Redis write failure.");
        }

        Version = Math.Max(Version ?? -1, tokenVersion);
        _handler?.Invoke(userId, Version.Value);
        return Task.FromResult(Version.Value);
    }

    public void Notify(Guid userId, int tokenVersion) => _handler?.Invoke(userId, tokenVersion);
}

file sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

file sealed class TokenCacheTenantContext : ITenantContext
{
    public Guid? CompetitionId => null;
    public void SetCompetitionId(Guid? id) { }
}
