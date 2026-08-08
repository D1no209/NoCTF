using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Scoring;

public sealed class FusionLeaderboardCacheTests
{
    [Test]
    public async Task Refreshes_and_reads_the_current_revision(
        CancellationToken cancellationToken)
    {
        var fixture = await CreateFixtureAsync(cancellationToken);
        using var fusion = new FusionCache(new FusionCacheOptions());
        await using var db = new NoCtfDbContext(fixture.Options);
        var cache = CreateCache(db, new CountingProjectionEngine(), fusion);

        await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);
        var snapshot = await cache.GetAsync(fixture.CompetitionId, cancellationToken);

        await Assert.That(snapshot).IsNotNull();
        await Assert.That(snapshot!.SnapshotRevision).IsEqualTo(1);
        await Assert.That(snapshot.Stale).IsFalse();
    }

    [Test]
    public async Task Revision_key_prevents_an_old_snapshot_from_satisfying_a_new_revision(
        CancellationToken cancellationToken)
    {
        var fixture = await CreateFixtureAsync(cancellationToken);
        using var fusion = new FusionCache(new FusionCacheOptions());
        await using (var db = new NoCtfDbContext(fixture.Options))
            await CreateCache(db, new CountingProjectionEngine(), fusion)
                .RefreshAsync(fixture.CompetitionId, cancellationToken);

        await using (var db = new NoCtfDbContext(fixture.Options))
        {
            var competition = await db.Competitions.SingleAsync(cancellationToken);
            competition.LeaderboardRevision = 2;
            await db.SaveChangesAsync(cancellationToken);
        }
        await using (var db = new NoCtfDbContext(fixture.Options))
        {
            var cache = CreateCache(db, new CountingProjectionEngine(), fusion);
            await Assert.That(await cache.GetAsync(
                fixture.CompetitionId,
                cancellationToken)).IsNull();
            await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);
            await Assert.That((await cache.GetAsync(
                fixture.CompetitionId,
                cancellationToken))!.SnapshotRevision).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Concurrent_refreshes_use_FusionCache_single_flight(
        CancellationToken cancellationToken)
    {
        var fixture = await CreateFixtureAsync(cancellationToken);
        using var fusion = new FusionCache(new FusionCacheOptions());
        var engine = new CountingProjectionEngine(TimeSpan.FromMilliseconds(150));
        await using var firstDb = new NoCtfDbContext(fixture.Options);
        await using var secondDb = new NoCtfDbContext(fixture.Options);
        var first = CreateCache(firstDb, engine, fusion);
        var second = CreateCache(secondDb, engine, fusion);

        await Task.WhenAll(
            first.RefreshAsync(fixture.CompetitionId, cancellationToken),
            second.RefreshAsync(fixture.CompetitionId, cancellationToken));

        await Assert.That(engine.InvocationCount).IsEqualTo(1);
    }

    private static FusionLeaderboardCache CreateCache(
        NoCtfDbContext db,
        ILeaderboardProjectionEngine engine,
        IFusionCache fusion) =>
        new(
            db,
            engine,
            new ConfigurationBuilder().Build(),
            new NullPublisher(),
            fusion);

    private static async Task<Fixture> CreateFixtureAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase($"fusion-leaderboard-{Guid.NewGuid():N}")
            .Options;
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddMilliseconds(1));
        await using var db = new NoCtfDbContext(options);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "fusion-owner",
            NormalizedUserName = "FUSION-OWNER",
            Email = "fusion-owner@example.test",
            NormalizedEmail = "FUSION-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Fusion leaderboard cache",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            ConfigurationJson = "{}",
            ConfigurationUpdatedAt = now,
            LeaderboardRevision = 1,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(options, competitionId);
    }

    private sealed record Fixture(
        DbContextOptions<NoCtfDbContext> Options,
        Guid CompetitionId);

    private sealed class CountingProjectionEngine(TimeSpan? delay = null)
        : ILeaderboardProjectionEngine
    {
        private int invocationCount;
        public int InvocationCount => invocationCount;

        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input)
        {
            Interlocked.Increment(ref invocationCount);
            if (delay is { } value)
                Thread.Sleep(value);
            return new([], []);
        }
    }

    private sealed class NullPublisher : ILeaderboardRefreshPublisher
    {
        public Task PublishAsync(
            Guid competitionId,
            DateTimeOffset generatedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
