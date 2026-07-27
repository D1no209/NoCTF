using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RedisRunnerCapacityGateTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Runner_registration_initializes_and_renews_capacity_without_overwriting_claims(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-registration";
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var registration = new RunnerAvailabilityRegistration(
                pool,
                runner,
                RuntimeProvider.Docker,
                "test-version",
                new RuntimeResourceLimits(1024, 100, 10),
                TimeSpan.FromSeconds(2),
                HasActiveAssignments: false);

            var initialized = await registry.RegisterAsync(registration, cancellationToken);

            await Assert.That(initialized).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            await Assert.That(await database.SetContainsAsync($"runner-pool:{pool}:members", runner)).IsTrue();
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:heartbeat")).IsTrue();
            await Assert.That((long)(await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes"))!).IsEqualTo(1024);
            await Assert.That(await database.KeyTimeToLiveAsync($"runner:{runner}:capacity")).IsNotNull();

            var gate = new RedisRunnerCapacityGate(redis);
            var request = new RunnerCapacityRequest(Guid.CreateVersion7(), pool, 512, 40, 2);
            await gate.TryClaimAsync(request, cancellationToken);
            var renewed = await registry.RegisterAsync(
                registration with { HasActiveAssignments = true },
                cancellationToken);

            await Assert.That(renewed).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            await Assert.That((long)(await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes"))!).IsEqualTo(512);

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:heartbeat")).IsFalse();
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:capacity")).IsFalse();
            await Assert.That(await database.SetContainsAsync($"runner-pool:{pool}:members", runner)).IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runner_registration_stays_offline_when_capacity_is_missing_with_active_assignments(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-active";
            var registry = new RedisRunnerAvailabilityRegistry(redis);

            var outcome = await registry.RegisterAsync(
                new RunnerAvailabilityRegistration(
                    pool,
                    runner,
                    RuntimeProvider.Docker,
                    "test-version",
                    new RuntimeResourceLimits(1024, 100, 10),
                    TimeSpan.FromMinutes(1),
                    HasActiveAssignments: true),
                cancellationToken);

            await Assert.That(outcome)
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted);
            await Assert.That(await database.SetContainsAsync($"runner-pool:{pool}:members", runner)).IsTrue();
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:heartbeat")).IsFalse();
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:capacity")).IsFalse();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runner_registration_clears_a_stale_heartbeat_before_rebuilding_untrusted_capacity(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-stale";
            await database.StringSetAsync($"runner:{runner}:heartbeat", "stale", TimeSpan.FromMinutes(1));
            await database.HashSetAsync($"runner:{runner}:capacity", Capacity(memory: 128));
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var registration = new RunnerAvailabilityRegistration(
                pool,
                runner,
                RuntimeProvider.Docker,
                "test-version",
                new RuntimeResourceLimits(1024, 100, 10),
                TimeSpan.FromMinutes(1),
                HasActiveAssignments: false);

            var first = await registry.RegisterAsync(registration, cancellationToken);
            var second = await registry.RegisterAsync(registration, cancellationToken);

            await Assert.That(first)
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted);
            await Assert.That(second).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:heartbeat")).IsTrue();
            await Assert.That((long)(await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes"))!).IsEqualTo(1024);
        });
    }

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

            var inventory = await gate.GetPoolInventoryAsync(pool, cancellationToken);
            await Assert.That(inventory.Availability)
                .IsEqualTo(RunnerPoolInventoryAvailability.Available);
            await Assert.That(inventory.RunnerIds).IsEquivalentTo([runner]);
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

            await database.HashSetAsync($"runner:{runner}:capacity", new HashEntry[]
            {
                new("availableMemoryBytes", 1024),
                new("availableNanoCpus", 100),
                new("availablePids", 10),
                new("totalMemoryBytes", 1024),
                new("totalNanoCpus", 100),
                new("totalPids", 10)
            });
            var lateRuntimeId = Guid.CreateVersion7();
            await database.HashSetAsync($"runner-claim:{lateRuntimeId:N}", new HashEntry[]
            {
                new("runnerId", runner),
                new("memoryBytes", 512),
                new("nanoCpus", 40),
                new("pidsLimit", 2)
            });

            await gate.ReleaseAsync(lateRuntimeId, runner, cancellationToken);

            await Assert.That((long)(await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes"))!).IsEqualTo(1024);
        });
    }

    private static HashEntry[] Capacity(long memory) =>
    [
        new("availableMemoryBytes", memory),
        new("availableNanoCpus", 100),
        new("availablePids", 10)
    ];
}
