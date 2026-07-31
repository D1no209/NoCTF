using Microsoft.Extensions.Configuration;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Scoring;

[Category("Integration")]
public sealed class RedisLeaderboardSubscriptionRegistryTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Registry_tracks_first_active_concurrent_subscribers_disconnects_and_expiry(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Leaderboard:SubscriberTtlSeconds"] = "1"
                })
                .Build();
            var registry = new RedisLeaderboardSubscriptionRegistry(redis, configuration);
            var competitionId = Guid.CreateVersion7();

            await Assert.That(await registry.TouchAsync(
                competitionId,
                "connection-1",
                cancellationToken)).IsTrue();
            await Assert.That(await registry.TouchAsync(
                competitionId,
                "connection-1",
                cancellationToken)).IsFalse();
            await Assert.That(await registry.TouchAsync(
                competitionId,
                "connection-2",
                cancellationToken)).IsFalse();
            await registry.RemoveAsync(competitionId, "connection-1", cancellationToken);
            await Assert.That(await registry.HasActiveAsync(
                competitionId,
                cancellationToken)).IsTrue();
            await registry.RemoveAsync(competitionId, "connection-2", cancellationToken);
            await Assert.That(await registry.HasActiveAsync(
                competitionId,
                cancellationToken)).IsFalse();

            var concurrentCompetitionId = Guid.CreateVersion7();
            var concurrentTouches = await Task.WhenAll(
                Enumerable.Range(0, 32).Select(index => registry.TouchAsync(
                    concurrentCompetitionId,
                    $"connection-{index}",
                    cancellationToken)));
            await Assert.That(concurrentTouches.Count(becameActive => becameActive))
                .IsEqualTo(1);
            await Assert.That(await registry.HasActiveAsync(
                concurrentCompetitionId,
                cancellationToken)).IsTrue();

            var expiringCompetitionId = Guid.CreateVersion7();
            await Assert.That(await registry.TouchAsync(
                expiringCompetitionId,
                "expiring-connection",
                cancellationToken)).IsTrue();
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            await Assert.That(await registry.HasActiveAsync(
                expiringCompetitionId,
                cancellationToken)).IsFalse();
            await Assert.That(await registry.TouchAsync(
                expiringCompetitionId,
                "replacement-connection",
                cancellationToken)).IsTrue();
        });
    }
}
