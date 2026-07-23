using NoCTF.Application.Runtime.Ports;
using NoCTF.Infrastructure.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RedisRunnerCapacityGateTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Heartbeat_status_requires_pool_membership_and_a_live_key(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-heartbeat";
            await database.SetAddAsync($"runner-pool:{pool}:members", runner);
            await database.StringSetAsync(
                $"runner:{runner}:heartbeat", "alive", TimeSpan.FromMinutes(1));
            var gate = new RedisRunnerCapacityGate(redis);

            await Assert.That(await gate.GetHeartbeatAsync(pool, runner, cancellationToken))
                .IsEqualTo(RunnerHeartbeatStatus.Online);
            await database.KeyDeleteAsync($"runner:{runner}:heartbeat");
            await Assert.That(await gate.GetHeartbeatAsync(pool, runner, cancellationToken))
                .IsEqualTo(RunnerHeartbeatStatus.Offline);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Claim_for_node_never_assigns_another_pool_member(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string fullRunner = "runner-1";
            const string availableRunner = "runner-2";
            await database.SetAddAsync($"runner-pool:{pool}:members", [fullRunner, availableRunner]);
            await database.StringSetAsync($"runner:{fullRunner}:heartbeat", "alive", TimeSpan.FromMinutes(1));
            await database.StringSetAsync($"runner:{availableRunner}:heartbeat", "alive", TimeSpan.FromMinutes(1));
            await database.HashSetAsync($"runner:{fullRunner}:capacity", Capacity(memory: 0));
            await database.HashSetAsync($"runner:{availableRunner}:capacity", Capacity(memory: 1024));

            var gate = new RedisRunnerCapacityGate(redis);
            var request = new RunnerCapacityRequest(Guid.CreateVersion7(), pool, 512, 40, 2);
            var claim = await gate.TryClaimForRunnerAsync(request, fullRunner, cancellationToken);

            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Insufficient);
            await Assert.That(claim.RunnerId).IsNull();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Claim_requires_live_runner_and_release_restores_capacity(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-1";
            await database.SetAddAsync($"runner-pool:{pool}:members", runner);
            await database.StringSetAsync($"runner:{runner}:heartbeat", "alive", TimeSpan.FromMinutes(1));
            await database.HashSetAsync($"runner:{runner}:capacity", new HashEntry[]
            {
                new("availableMemoryBytes", 1024),
                new("availableNanoCpus", 100),
                new("availablePids", 10)
            });

            var gate = new RedisRunnerCapacityGate(redis);
            var runtimeId = Guid.CreateVersion7();
            var request = new RunnerCapacityRequest(runtimeId, pool, 512, 40, 2);
            var claim = await gate.TryClaimAsync(request, cancellationToken);

            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await Assert.That(claim.RunnerId).IsEqualTo(runner);
            await Assert.That(claim.State).IsEqualTo(RunnerCapacityClaimState.Acquired);
            var duplicate = await gate.TryClaimForRunnerAsync(request, runner, cancellationToken);
            await Assert.That(duplicate.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await Assert.That(duplicate.State).IsEqualTo(RunnerCapacityClaimState.AlreadyOwned);
            var afterDuplicate = await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes");
            await Assert.That((long)afterDuplicate!).IsEqualTo(512);
            var wrongOwner = await gate.ReleaseAsync(runtimeId, "runner-2", cancellationToken);
            await Assert.That(wrongOwner).IsEqualTo(RunnerCapacityReleaseOutcome.OwnerMismatch);
            var afterWrongOwner = await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes");
            await Assert.That((long)afterWrongOwner!).IsEqualTo(512);
            var release = await gate.ReleaseAsync(runtimeId, runner, cancellationToken);
            await Assert.That(release).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            var remaining = await database.HashGetAsync($"runner:{runner}:capacity", "availableMemoryBytes");
            await Assert.That((long)remaining!).IsEqualTo(1024);
        });
    }

    private static HashEntry[] Capacity(long memory) =>
    [
        new("availableMemoryBytes", memory),
        new("availableNanoCpus", 100),
        new("availablePids", 10)
    ];
}
