using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Net;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration"), NotInParallel]
public sealed class NatsLeaderboardPublicationFenceTests
{
    [Test, Timeout(300_000)]
    public async Task Optimistic_publication_preserves_previous_results_tracks_pending_work_and_rejects_regression(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var nats = new ContainerBuilder("nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(nats.StartAsync(ct), redis.StartAsync(ct));
            await using var connection = new NatsConnection(new NatsOpts { Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" });
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = redis.GetConnectionString() }).Build();
            await using var services = new ServiceCollection().AddNoCtfCaching(configuration, false).BuildServiceProvider();
            var caches = services.GetRequiredService<IFusionCacheProvider>();
            var fence = new NatsLeaderboardPublicationFence(connection, caches);
            await Assert.That(await fence.GetPendingCompetitionsAsync(ct)).IsEmpty();
            var id = Guid.NewGuid();
            await Assert.That(await fence.GetAsync(id, ct)).IsNull();
            var first = await fence.BeginBuildAsync(id, 1, ct);
            await Assert.That(await fence.TryPublishAsync(id, first, 10, "first", ct)).IsTrue();
            await fence.InvalidateAsync(id, 0, ct);
            var building = await fence.BeginBuildAsync(id, 1, ct);
            await fence.InvalidateAsync(id, 0, ct);
            await Assert.That(await fence.GetAsync(id, ct)).IsEqualTo(new LeaderboardFencedPayload(first.Version, "first"));
            await Assert.That(await fence.TryPublishAsync(id, building, 20, "building", ct)).IsTrue();
            await Assert.That(await fence.HasPendingAsync(id, ct)).IsTrue();
            await using var restartedConnection = new NatsConnection(new NatsOpts { Url = connection.Opts.Url });
            var restarted = new NatsLeaderboardPublicationFence(restartedConnection, caches);
            await Assert.That(await restarted.GetPendingCompetitionsAsync(ct)).Contains(id);
            using (var recoveredWorker = Host.CreateDefaultBuilder().ConfigureServices(registrations =>
            {
                registrations.AddSingleton<ILeaderboardPublicationFence>(restarted);
                registrations.AddHostedService<LeaderboardPendingProjectionRecoveryAgent>();
            }).UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.UseNats(connection.Opts.Url).AutoProvision().UseJetStream(_ => { })
                    .DefineStream("RECOVERY_TEST", stream =>
                    {
                        stream.WithSubject("recovery.projection");
                        stream.Retention = NATS.Client.JetStream.Models.StreamConfigRetention.Workqueue;
                    });
                options.PublishMessage<ProjectLeaderboard>().ToNatsSubject("recovery.projection").UseJetStream("RECOVERY_TEST");
            }).Build())
            {
                await recoveredWorker.StartAsync(ct);
                var stream = await connection.CreateJetStreamContext().GetStreamAsync("RECOVERY_TEST", cancellationToken: ct);
                for (var attempt = 0; stream.Info.State.Messages == 0 && attempt < 50; attempt++)
                {
                    await Task.Delay(100, ct);
                    stream = await connection.CreateJetStreamContext().GetStreamAsync("RECOVERY_TEST", cancellationToken: ct);
                }
                await Assert.That(stream.Info.State.Messages).IsGreaterThan(0);
                await recoveredWorker.StopAsync(ct);
            }
            var older = await fence.BeginBuildAsync(id, 1, ct);
            var newer = await fence.BeginBuildAsync(id, 1, ct);
            await Assert.That(await fence.TryPublishAsync(id, newer, 30, "newer", ct)).IsTrue();
            await Assert.That(await fence.TryPublishAsync(id, older, 25, "older", ct)).IsFalse();
            var backwards = await fence.BeginBuildAsync(id, 1, ct);
            await Assert.That(await fence.TryPublishAsync(id, backwards, 29, "backwards", ct)).IsFalse();
            await Assert.That(await fence.GetAsync(id, ct)).IsEqualTo(new LeaderboardFencedPayload(newer.Version, "newer"));
            await Assert.That(await fence.HasPendingAsync(id, ct)).IsFalse();
            var expiredAccess = await fence.BeginBuildAsync(id, 1, ct);
            await fence.InvalidateAccessAsync(id, 0, ct);
            await Assert.That(await fence.TryPublishAsync(id, expiredAccess, 40, "expired-access", ct)).IsFalse();
            var current = await fence.BeginBuildAsync(id, 1, ct);
            var duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fence.TryPublishAsync(id, current, 40, "current", ct)));
            await Assert.That(duplicates.All(value => value)).IsTrue();
            await Assert.That(await fence.GetAsync(id, ct)).IsEqualTo(new LeaderboardFencedPayload(current.Version, "current"));
            await caches.GetCache(NoCtfCacheNames.Leaderboards).RemoveAsync(NatsLeaderboardPublicationFence.PayloadKey(id, current.Version, "current"), token: ct);
            await Assert.That(await fence.GetAsync(id, ct)).IsNull();
            await Assert.That(await fence.HasPendingAsync(id, ct)).IsTrue();
        });
    }
}
