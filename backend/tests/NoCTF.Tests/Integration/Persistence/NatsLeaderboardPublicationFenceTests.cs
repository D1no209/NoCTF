using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration"), NotInParallel]
public sealed class NatsLeaderboardPublicationFenceTests
{
    [Test, Timeout(300_000)]
    public async Task Version_pointer_rejects_stale_publications_and_rebuilds_after_cache_loss(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await nats.StartAsync(ct);
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services.BuildServiceProvider();
            var fence = new NatsLeaderboardPublicationFence(connection,
                cacheServices.GetRequiredService<IFusionCacheProvider>());
            var competitionId = Guid.NewGuid();
            const long floor = 638_916_123_456_789_012;

            var older = await fence.IssueAsync(competitionId, floor, ct);
            var newer = await fence.IssueAsync(competitionId, floor, ct);
            await Assert.That(older).IsEqualTo(floor);
            await Assert.That(newer).IsEqualTo(floor + 1);
            await Assert.That(await fence.TryCommitAsync(
                competitionId, newer, "newer", ct)).IsTrue();
            await Assert.That(await fence.TryCommitAsync(
                competitionId, older, "older", ct)).IsFalse();
            await Assert.That(await fence.GetAsync(competitionId, ct))
                .IsEqualTo(new LeaderboardFencedPayload(newer, "newer"));
            await Assert.That(await fence.IsCurrentAsync(
                competitionId, newer, ct)).IsTrue();

            await fence.InvalidateAsync(competitionId, newer + 1, ct);
            await Assert.That(await fence.GetAsync(competitionId, ct)).IsNull();
            var current = await fence.IssueAsync(competitionId, newer + 1, ct);
            await Assert.That(await fence.TryCommitAsync(
                competitionId, current, "current", ct)).IsTrue();
            await Assert.That(await fence.GetAsync(competitionId, ct))
                .IsEqualTo(new LeaderboardFencedPayload(current, "current"));

            await cacheServices.GetRequiredService<IFusionCacheProvider>()
                .GetCache(NoCtfCacheNames.Leaderboards)
                .RemoveAsync($"scoreboard:published:{competitionId:N}:{current}",
                    token: ct);
            await Assert.That(await fence.GetAsync(competitionId, ct)).IsNull();
            var rebuilt = await fence.IssueAsync(competitionId, current + 1, ct);
            await Assert.That(await fence.TryCommitAsync(
                competitionId, rebuilt, "rebuilt", ct)).IsTrue();
            await Assert.That(await fence.GetAsync(competitionId, ct))
                .IsEqualTo(new LeaderboardFencedPayload(rebuilt, "rebuilt"));
        });
    }
}
