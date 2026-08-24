using NoCTF.Infrastructure.Scoring.Leaderboard;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class RedisLeaderboardPublicationFenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Fence_uses_exact_int64_versions_and_is_strictly_increasing(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            var fence = new RedisLeaderboardPublicationFence(redis);
            var competitionId = Guid.CreateVersion7();
            const long floor = 638_916_123_456_789_012;

            var first = await fence.IssueAsync(competitionId, floor, cancellationToken);
            var second = await fence.IssueAsync(competitionId, floor, cancellationToken);

            await Assert.That(first).IsEqualTo(floor);
            await Assert.That(second).IsEqualTo(floor + 1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Older_publication_cannot_replace_a_newer_payload(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            var fence = new RedisLeaderboardPublicationFence(redis);
            var competitionId = Guid.CreateVersion7();
            var older = await fence.IssueAsync(competitionId, 100, cancellationToken);
            var newer = await fence.IssueAsync(competitionId, 100, cancellationToken);

            var newerAccepted = await fence.TryCommitAsync(
                competitionId, newer, "newer", cancellationToken);
            var olderAccepted = await fence.TryCommitAsync(
                competitionId, older, "older", cancellationToken);
            var published = await fence.GetAsync(competitionId, cancellationToken);

            await Assert.That(newerAccepted).IsTrue();
            await Assert.That(olderAccepted).IsFalse();
            await Assert.That(published).IsEqualTo(new LeaderboardFencedPayload(newer, "newer"));
            await Assert.That(await fence.IsCurrentAsync(
                competitionId, newer, cancellationToken)).IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Redis_data_loss_reseeds_the_fence_from_the_projection_version(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            var fence = new RedisLeaderboardPublicationFence(redis);
            var database = redis.GetDatabase();
            var competitionId = Guid.CreateVersion7();
            var previous = await fence.IssueAsync(competitionId, 1_000, cancellationToken);
            await fence.TryCommitAsync(competitionId, previous, "old", cancellationToken);
            await database.KeyDeleteAsync(
                [
                    $"leaderboard:{competitionId:N}:fence",
                    $"leaderboard:{competitionId:N}:published"
                ]);

            var reseeded = await fence.IssueAsync(
                competitionId,
                previous + 10_000,
                cancellationToken);

            await Assert.That(reseeded).IsEqualTo(previous + 10_000);
            await Assert.That(await fence.GetAsync(competitionId, cancellationToken)).IsNull();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Invalidation_tombstone_rejects_an_inflight_stale_projection(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            var fence = new RedisLeaderboardPublicationFence(redis);
            var competitionId = Guid.CreateVersion7();
            var stale = await fence.IssueAsync(competitionId, 100, cancellationToken);

            await fence.InvalidateAsync(competitionId, 101, cancellationToken);
            var staleAccepted = await fence.TryCommitAsync(
                competitionId,
                stale,
                "stale",
                cancellationToken);
            var current = await fence.IssueAsync(competitionId, 101, cancellationToken);
            var currentAccepted = await fence.TryCommitAsync(
                competitionId,
                current,
                "current",
                cancellationToken);

            await Assert.That(staleAccepted).IsFalse();
            await Assert.That(currentAccepted).IsTrue();
            await Assert.That(await fence.GetAsync(competitionId, cancellationToken))
                .IsEqualTo(new LeaderboardFencedPayload(current, "current"));
        });
    }
}
