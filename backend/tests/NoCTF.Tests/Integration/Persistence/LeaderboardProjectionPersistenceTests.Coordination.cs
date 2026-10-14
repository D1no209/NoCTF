using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Net;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Worker;
using NSubstitute;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000), NotInParallel]
    public async Task Independent_nodes_share_cold_rebuilds_and_invalidated_events_but_forced_refresh_stays_authoritative(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), redis.StartAsync(ct), nats.StartAsync(ct));
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, now);
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(ct);
                setup.Users.Add(owner); setup.Competitions.Add(fixture.Competition); setup.Challenges.Add(fixture.Challenge);
                setup.CompetitionChallenges.Add(fixture.CompetitionChallenge); setup.Teams.Add(fixture.Team);
                setup.GameplayFacts.AddRange(fixture.Facts); await setup.SaveChangesAsync(ct);
            }
            var nodes = Enumerable.Range(0, 4).Select(_ => new ProjectionNode(
                $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}", redis.GetConnectionString())).ToArray();
            var engine = new CountingProjectionEngine();
            var samples = new List<object>();
            FusionLeaderboardCache Cache(NoCtfDbContext db, ProjectionNode node, bool coordinated) => new(db, engine,
                Substitute.For<ILeaderboardRefreshPublisher>(), node.Caches, node.Fence, node.Local,
                snapshotDeserializer: node.Decoder, rebuildCoordinator: coordinated ? node.Coordinator : null);
            try
            {
                // Interleaved component contention diagnostics, not HTTP QPS or a capacity benchmark.
                for (var repetition = 0; repetition < 3; repetition++)
                foreach (var coordinated in repetition % 2 == 0 ? new[] { false, true } : new[] { true, false })
                {
                    await nodes[0].Fence.InvalidateAsync(fixture.Competition.Id, DateTimeOffset.UtcNow.UtcTicks, ct);
                    var before = engine.Count;
                    var ready = await Task.WhenAll(Enumerable.Range(0, 16).Select(async index =>
                    {
                        await using var db = new NoCtfDbContext(options);
                        return await Cache(db, nodes[index % nodes.Length], coordinated).GetScoreboardAsync(fixture.Competition.Id, ct);
                    }));
                    var computations = engine.Count - before;
                    if (coordinated)
                    {
                        await Assert.That(computations).IsEqualTo(1);
                        await Assert.That(ready.All(value => value is not null)).IsTrue();
                        await Assert.That(ready.Select(value => value!.Snapshot.Version).Distinct().Count()).IsEqualTo(1);
                    }
                    foreach (var value in ready.OfType<ScoreboardProjection>())
                        await Assert.That(value.Snapshot.Teams.Single().TotalScore).IsEqualTo(0L);
                    samples.Add(new { repetition, coordinated, nodes = nodes.Length, readers = ready.Length,
                        computations, immediatelyReady = ready.Count(value => value is not null) });
                }
                await nodes[0].Fence.InvalidateAsync(fixture.Competition.Id, DateTimeOffset.UtcNow.UtcTicks, ct);
                var eventBefore = engine.Count;
                await Task.WhenAll(nodes.Select(async node =>
                {
                    await using var db = new NoCtfDbContext(options);
                    await new LeaderboardMessageHandler(Cache(db, node, true), db, TimeProvider.System)
                        .Handle(new(fixture.Competition.Id, LeaderboardRefreshMode.IfMissing), ct);
                }));
                await Assert.That(engine.Count - eventBefore).IsEqualTo(1);

                await using var writer = new NoCtfDbContext(options);
                var fact = await writer.GameplayFacts.SingleAsync(value => value.Id == fixture.Facts[0].Id, ct);
                fact.Result = GameplayFactResult.Correct;
                await writer.SaveChangesAsync(ct);
                var forced = Cache(writer, nodes[0], true);
                var forcedBefore = engine.Count;
                await forced.RefreshAsync(fixture.Competition.Id, ct); // Deliberately no invalidation.
                await Assert.That(engine.Count - forcedBefore).IsEqualTo(1);
                await Assert.That((await forced.GetScoreboardAsync(fixture.Competition.Id, ct))!.Snapshot.Teams.Single().TotalScore)
                    .IsGreaterThan(0L);

                var directory = Environment.GetEnvironmentVariable("NOCTF_PERFORMANCE_ARTIFACTS")
                    ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "performance");
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "leaderboard-rebuild-contention.json"), JsonSerializer.Serialize(new
                {
                    baseline = "75f8790b: same reader with cluster coordination disabled", workload = "4 independent NATS clients and Redis L1 caches, 16 concurrent cold readers, 250 facts",
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    logicalProcessors = Environment.ProcessorCount, samples
                }, new JsonSerializerOptions { WriteIndented = true }), ct);
            }
            finally { foreach (var node in nodes) await node.DisposeAsync(); }
        });
    }

    [Test, Timeout(120_000)]
    public async Task Lost_rebuild_ownership_cancels_work_and_cannot_release_its_successor(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await nats.StartAsync(ct);
            await using var connection = new NatsConnection(new NatsOpts { Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" });
            var first = new LeaderboardRebuildCoordinator(connection, TimeProvider.System);
            var second = new LeaderboardRebuildCoordinator(connection, TimeProvider.System);
            var id = Guid.NewGuid();
            var old = await first.AcquireAsync(id, ct);
            var store = await connection.CreateKeyValueStoreContext().GetStoreAsync("NOCTF_LEASES_V2", ct);
            await store.DeleteAsync($"leaderboard-rebuild-{id:N}", cancellationToken: ct);
            await using var successor = await second.AcquireAsync(id, ct);
            await Assert.That(async () => await old.ConfirmOwnershipAsync()).Throws<TimeoutException>();
            await Assert.That(old.Token.IsCancellationRequested).IsTrue();
            await old.DisposeAsync();
            await successor.ConfirmOwnershipAsync();
            await using var unrelated = await first.AcquireAsync(Guid.NewGuid(), ct);
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.That(async () => await first.AcquireAsync(id, cancelled.Token)).Throws<OperationCanceledException>();
        });
    }

    private sealed class CountingProjectionEngine : ILeaderboardProjectionEngine
    {
        private readonly LeaderboardProjectionEngine inner = new(new LeaderboardProjectorCatalog());
        private int count;
        public int Count => Volatile.Read(ref count);
        public ScoreboardProjection Project(LeaderboardProjectionInput input)
        {
            Interlocked.Increment(ref count);
            return inner.Project(input);
        }
    }

    private sealed class ProjectionNode : IAsyncDisposable
    {
        private readonly NatsConnection connection;
        private readonly ServiceProvider services;
        public IFusionCacheProvider Caches { get; }
        public NatsLeaderboardPublicationFence Fence { get; }
        public LeaderboardProjectionKeyedLock Local { get; } = new();
        public LeaderboardSnapshotDeserializer Decoder { get; } = new();
        public LeaderboardRebuildCoordinator Coordinator { get; }
        public ProjectionNode(string nats, string redis)
        {
            connection = new(new NatsOpts { Url = nats });
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Redis"] = redis }).Build();
            services = new ServiceCollection().AddNoCtfCaching(configuration, false).BuildServiceProvider();
            Caches = services.GetRequiredService<IFusionCacheProvider>();
            Fence = new(connection, Caches);
            Coordinator = new(connection, TimeProvider.System);
        }
        public async ValueTask DisposeAsync()
        {
            Local.Dispose(); Decoder.Dispose();
            await services.DisposeAsync(); await connection.DisposeAsync();
        }
    }
}
