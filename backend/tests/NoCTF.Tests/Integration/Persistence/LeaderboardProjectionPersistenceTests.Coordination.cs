using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000), NotInParallel]
    public async Task Independent_readers_keep_published_scores_until_worker_refresh_and_access_changes_hide_old_material(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await using var nats = new ContainerBuilder("nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), redis.StartAsync(ct), nats.StartAsync(ct));
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, now);
            var rules = fixture.CompetitionChallenge.Rules ?? throw new InvalidOperationException("Missing CTF rules.");
            rules.HasScoreCurve = true;
            rules.ScoreCurve = new() { InitialPoints = 100, MinimumPoints = 100, DecayMode = PersistedScoreDecayMode.Fixed };
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            db.Users.Add(owner); db.Competitions.Add(fixture.Competition); db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge); db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(fixture.Facts); await db.SaveChangesAsync(ct);
            await using var connection = new NatsConnection(new NatsOpts { Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" });
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = redis.GetConnectionString() }).Build();
            await using var services = new ServiceCollection().AddNoCtfCaching(configuration, false).BuildServiceProvider();
            var caches = services.GetRequiredService<IFusionCacheProvider>();
            var fence = new NatsLeaderboardPublicationFence(connection, caches);
            FusionLeaderboardCache Cache(NoCtfDbContext context) => new(context,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()), new NatsLeaderboardRefreshPublisher(connection), caches, fence);
            var cache = Cache(db);
            await Assert.That(await cache.GetScoreboardAsync(fixture.Competition.Id, ct)).IsNull();
            await cache.RefreshAsync(fixture.Competition.Id, ct);
            var original = (await cache.GetScoreboardAsync(fixture.Competition.Id, ct))!;
            await Assert.That(original.Snapshot.Teams.Single().TotalScore).IsEqualTo(0L);
            fixture.Facts[0].Result = GameplayFactResult.Correct;
            await db.SaveChangesAsync(ct);
            await cache.InvalidateAsync(fixture.Competition.Id, ct);
            var readers = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
            {
                await using var readerDb = new NoCtfDbContext(options);
                return await Cache(readerDb).GetScoreboardAsync(fixture.Competition.Id, ct);
            }));
            foreach (var value in readers)
            {
                await Assert.That(value!.Snapshot.Version).IsEqualTo(original.Snapshot.Version);
                await Assert.That(value.Snapshot.Teams.Single().TotalScore).IsEqualTo(0L);
            }
            await cache.RefreshAsync(fixture.Competition.Id, ct);
            var refreshed = (await cache.GetScoreboardAsync(fixture.Competition.Id, ct))!;
            await Assert.That(refreshed.Snapshot.Version).IsGreaterThan(original.Snapshot.Version);
            await Assert.That(refreshed.Snapshot.Teams.Single().TotalScore).IsEqualTo(100L);
            fixture.CompetitionChallenge.IsPublished = false;
            await db.SaveChangesAsync(ct);
            await Assert.That(await cache.GetScoreboardAsync(fixture.Competition.Id, ct)).IsNull();
            await cache.RefreshAsync(fixture.Competition.Id, ct);
            var hidden = (await cache.GetScoreboardAsync(fixture.Competition.Id, ct))!;
            await Assert.That(hidden.ParticipantView!.ChallengeCatalog.Challenges.Count).IsEqualTo(0);
        });
    }
}
