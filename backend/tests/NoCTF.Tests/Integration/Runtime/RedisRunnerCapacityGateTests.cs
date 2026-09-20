using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using StackExchange.Redis;
using Testcontainers.Redis;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using NoCTF.Application.Observability;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RedisRunnerCapacityGateTests
{
    [Test, Timeout(300_000)]
    public async Task Quota_changes_preserve_claims_and_report_negative_budget_without_killing_work(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var registration = new RunnerAvailabilityRegistration("quota-change", "quota-runner", RuntimeProvider.Docker,
                "test", new(1024, 100, 10), TimeSpan.FromSeconds(2), false);
            await registry.RegisterAsync(registration, ct);
            var gate = new RedisRunnerCapacityGate(redis);
            var id = Guid.NewGuid();
            await gate.TryClaimAsync(new(id, registration.RunnerPool, 800, 80, 8), ct);
            var legacy = await new RedisRunnerCapacityLedger(redis).ReadLegacyAsync(
                new RuntimeInstance { Id = id, RuntimeKind = RuntimeKind.Container }, registration.RunnerId, ct);
            await Assert.That(legacy!.Budget).IsEqualTo(new RuntimeResourceAmount(800, 80, 8));
            await Assert.That(legacy.Limit).IsEqualTo(legacy.Budget);
            var smaller = registration with { Capacity = new(512, 50, 5), HasActiveAssignments = true };
            await Assert.That(await registry.RegisterAsync(smaller, ct)).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            var db = redis.GetDatabase();
            await Assert.That((long)await db.HashGetAsync("runner:quota-runner:capacity", "availableMemoryBytes")).IsEqualTo(-288);
            await Assert.That((await gate.TryClaimAsync(new(Guid.NewGuid(), registration.RunnerPool, 1, 1, 1), ct)).Availability)
                .IsEqualTo(RunnerCapacityAvailability.Insufficient);
            await registry.RegisterAsync(registration with { Capacity = new(2048, 200, 20), HasActiveAssignments = true }, ct);
            await Assert.That((long)await db.HashGetAsync("runner:quota-runner:capacity", "availableMemoryBytes")).IsEqualTo(1248);
            await gate.ReleaseAsync(id, registration.RunnerId, ct);
            await gate.ReleaseAsync(id, registration.RunnerId, ct);
            await Assert.That((long)await db.HashGetAsync("runner:quota-runner:capacity", "availableMemoryBytes")).IsEqualTo(2048);
            await registry.RegisterAsync(registration with { ProviderAvailable = false }, ct);
            await Assert.That(await db.KeyExistsAsync("runner:quota-runner:heartbeat")).IsTrue();
            await Assert.That(await db.KeyTimeToLiveAsync("runner:quota-runner:capacity")).IsNull();
            await Assert.That((await gate.TryClaimAsync(new(Guid.NewGuid(), registration.RunnerPool, 1, 1, 1), ct)).Availability)
                .IsEqualTo(RunnerCapacityAvailability.Insufficient);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Exported_pool_quota_follows_real_claim_release_and_offline_heartbeats(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var pool = $"quota-{Guid.NewGuid():N}";
            var runner = $"runner-{Guid.NewGuid():N}";
            var readings = new ConcurrentDictionary<string, long>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, owner) =>
            {
                if (instrument.Meter.Name == NoCtfTelemetry.MeterName) owner.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                string group = "", resource = "";
                foreach (var tag in tags)
                {
                    if (tag.Key == "pool") group = tag.Value?.ToString() ?? "";
                    if (tag.Key == "resource") resource = tag.Value?.ToString() ?? "";
                }
                if (group == pool) readings[instrument.Name + ":" + resource] = value;
            });
            listener.Start();
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var registration = new RunnerAvailabilityRegistration(pool, runner, RuntimeProvider.Docker, "test-version",
                new RuntimeResourceLimits(1024, 100, 10), TimeSpan.FromMinutes(1), HasActiveAssignments: false);
            await registry.RegisterAsync(registration, ct);
            listener.RecordObservableInstruments();
            await Assert.That(readings["noctf.runner.capacity.available:memory"]).IsEqualTo(1024);
            var gate = new RedisRunnerCapacityGate(redis);
            var runtime = Guid.NewGuid();
            await Assert.That((await gate.TryClaimAsync(new(runtime, pool, 512, 40, 2), ct)).Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await registry.RegisterAsync(registration with { HasActiveAssignments = true }, ct);
            listener.RecordObservableInstruments();
            await Assert.That(readings["noctf.runner.capacity.available:memory"]).IsEqualTo(512);
            await Assert.That(readings["noctf.runner.capacity.available:cpu"]).IsEqualTo(60);
            await Assert.That(readings["noctf.runner.capacity.available:pids"]).IsEqualTo(8);
            await gate.ReleaseAsync(runtime, runner, ct);
            await registry.RegisterAsync(registration, ct);
            listener.RecordObservableInstruments();
            await Assert.That(readings["noctf.runner.capacity.available:memory"]).IsEqualTo(1024);
            await registry.RegisterAsync(registration with { ProviderAvailable = false }, ct);
            listener.RecordObservableInstruments();
            await Assert.That(readings["noctf.runner.online:"]).IsEqualTo(0);
            await Assert.That(readings["noctf.runner.capacity.total:memory"]).IsEqualTo(0);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runner_registration_initializes_and_renews_capacity_without_overwriting_claims(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
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
            var initialCandidateScore = await database.SortedSetScoreAsync(
                $"runner-pool:{pool}:candidates",
                runner);
            await Assert.That(initialCandidateScore).IsNotNull();
            await Assert.That(initialCandidateScore!.Value).IsLessThan(0.0000011d);
            await Assert.That((long)(await database.HashGetAsync(
                $"runner:{runner}:capacity", "availableMemoryBytes"))!).IsEqualTo(1024);
            await Assert.That(await database.KeyTimeToLiveAsync($"runner:{runner}:capacity")).IsNull();

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
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:capacity")).IsTrue();
            await Assert.That(await database.SetContainsAsync($"runner-pool:{pool}:members", runner)).IsTrue();
            var staleClaim = await gate.TryClaimAsync(
                new RunnerCapacityRequest(Guid.CreateVersion7(), pool, 1, 1, 1),
                cancellationToken);
            await Assert.That(staleClaim.Availability)
                .IsEqualTo(RunnerCapacityAvailability.Insufficient);
            await Assert.That(await database.SortedSetScoreAsync(
                $"runner-pool:{pool}:candidates",
                runner)).IsNull();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runner_registration_stays_offline_when_capacity_is_missing_with_active_assignments(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
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
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:heartbeat")).IsTrue();
            await Assert.That(await database.KeyExistsAsync($"runner:{runner}:capacity")).IsFalse();
            await Assert.That(await database.SortedSetScoreAsync(
                $"runner-pool:{pool}:candidates",
                runner)).IsNull();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runner_registration_keeps_liveness_but_requires_reconciliation_of_untrusted_capacity(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
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
            await database.KeyDeleteAsync([$"runner:{runner}:heartbeat", $"runner:{runner}:capacity"]);
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
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
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
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
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
    public async Task Pool_claim_selects_the_lowest_pressure_candidate(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string busyRunner = "runner-a-busy";
            const string idleRunner = "runner-z-idle";
            await RegisterRunnerAsync(database, pool, busyRunner);
            await RegisterRunnerAsync(database, pool, idleRunner);
            await database.HashSetAsync($"runner:{busyRunner}:capacity",
            [
                new("availableMemoryBytes", 128),
                new("availableNanoCpus", 20),
                new("availablePids", 2)
            ]);
            await database.SortedSetAddAsync(
                $"runner-pool:{pool}:candidates",
                [new(busyRunner, 0.875d), new(idleRunner, 0d)]);

            var gate = new RedisRunnerCapacityGate(redis);
            var claim = await gate.TryClaimAsync(
                new RunnerCapacityRequest(Guid.CreateVersion7(), pool, 64, 10, 1),
                cancellationToken);

            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await Assert.That(claim.RunnerId).IsEqualTo(idleRunner);
            await AssertCapacityAsync(database, busyRunner, memory: 128, nanoCpus: 20, pids: 2);
            await AssertCapacityAsync(database, idleRunner, memory: 960, nanoCpus: 90, pids: 9);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Pool_claim_rebuilds_the_index_when_top_candidates_cannot_fit(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            for (var index = 0; index < 8; index++)
            {
                var runner = $"runner-full-{index}";
                await RegisterRunnerAsync(database, pool, runner);
                await database.HashSetAsync($"runner:{runner}:capacity", "availableMemoryBytes", 0);
                await database.SortedSetAddAsync(
                    $"runner-pool:{pool}:candidates",
                    runner,
                    index / 100d);
            }

            const string availableRunner = "runner-available-ninth";
            await RegisterRunnerAsync(database, pool, availableRunner);
            await database.SortedSetRemoveAsync($"runner-pool:{pool}:candidates", availableRunner);

            var gate = new RedisRunnerCapacityGate(redis);
            var claim = await gate.TryClaimAsync(
                new RunnerCapacityRequest(Guid.CreateVersion7(), pool, 512, 40, 2),
                cancellationToken);

            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await Assert.That(claim.RunnerId).IsEqualTo(availableRunner);
            await Assert.That(await database.SortedSetLengthAsync(
                $"runner-pool:{pool}:candidates")).IsEqualTo(9);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Claim_requires_live_runner_and_release_restores_capacity(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
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
                new("availablePids", 10),
                new("totalMemoryBytes", 1024),
                new("totalNanoCpus", 100),
                new("totalPids", 10)
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
            var restoredCandidateScore = await database.SortedSetScoreAsync(
                $"runner-pool:{pool}:candidates",
                runner);
            await Assert.That(restoredCandidateScore).IsNotNull();
            await Assert.That(restoredCandidateScore!.Value).IsLessThan(0.0000011d);

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

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_claims_never_overcommit_and_releases_restore_exact_capacity(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-concurrent";
            await RegisterRunnerAsync(database, pool, runner);
            var gate = new RedisRunnerCapacityGate(redis);
            var runtimeIds = Enumerable.Range(0, 64)
                .Select(_ => Guid.CreateVersion7())
                .ToArray();

            var claims = await Task.WhenAll(runtimeIds.Select(runtimeId =>
                gate.TryClaimForRunnerAsync(
                    new RunnerCapacityRequest(runtimeId, pool, 128, 10, 1),
                    runner,
                    cancellationToken)));

            var claimedRuntimeIds = runtimeIds
                .Zip(claims)
                .Where(pair => pair.Second.Availability == RunnerCapacityAvailability.Claimed)
                .Select(pair => pair.First)
                .ToArray();
            await Assert.That(claimedRuntimeIds).Count().IsEqualTo(8);
            await Assert.That(claims.Count(claim =>
                claim.Availability == RunnerCapacityAvailability.Insufficient)).IsEqualTo(56);
            await AssertCapacityAsync(database, runner, memory: 0, nanoCpus: 20, pids: 2);

            var releases = await Task.WhenAll(claimedRuntimeIds.SelectMany(runtimeId =>
                new[]
                {
                    gate.ReleaseAsync(runtimeId, runner, cancellationToken),
                    gate.ReleaseAsync(runtimeId, runner, cancellationToken)
                }));

            await Assert.That(releases.Count(result =>
                result == RunnerCapacityReleaseOutcome.Released)).IsEqualTo(8);
            await Assert.That(releases.Count(result =>
                result == RunnerCapacityReleaseOutcome.AlreadyReleased)).IsEqualTo(8);
            await AssertCapacityAsync(database, runner, memory: 1024, nanoCpus: 100, pids: 10);
            foreach (var runtimeId in claimedRuntimeIds)
                await Assert.That(await database.KeyExistsAsync($"runner-claim:{runtimeId:N}")).IsFalse();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_replays_across_runners_debit_one_owner_only(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string firstRunner = "runner-a";
            const string secondRunner = "runner-b";
            await RegisterRunnerAsync(database, pool, firstRunner);
            await RegisterRunnerAsync(database, pool, secondRunner);
            var gate = new RedisRunnerCapacityGate(redis);
            var runtimeId = Guid.CreateVersion7();
            var request = new RunnerCapacityRequest(runtimeId, pool, 256, 25, 2);

            var claims = await Task.WhenAll(Enumerable.Range(0, 64).Select(index =>
                gate.TryClaimForRunnerAsync(
                    request,
                    index % 2 == 0 ? firstRunner : secondRunner,
                    cancellationToken)));

            var owner = claims
                .Where(claim => claim.Availability == RunnerCapacityAvailability.Claimed)
                .Select(claim => claim.RunnerId)
                .Distinct(StringComparer.Ordinal)
                .Single();
            var otherRunner = string.Equals(owner, firstRunner, StringComparison.Ordinal)
                ? secondRunner
                : firstRunner;
            await Assert.That(claims.Count(claim =>
                claim.State == RunnerCapacityClaimState.Acquired)).IsEqualTo(1);
            await Assert.That(claims.Count(claim =>
                claim.State == RunnerCapacityClaimState.AlreadyOwned)).IsEqualTo(31);
            await Assert.That(claims.Count(claim =>
                claim.Availability == RunnerCapacityAvailability.Insufficient)).IsEqualTo(32);
            await AssertCapacityAsync(database, owner!, memory: 768, nanoCpus: 75, pids: 8);
            await AssertCapacityAsync(database, otherRunner, memory: 1024, nanoCpus: 100, pids: 10);

            var wrongOwnerRelease = await gate.ReleaseAsync(runtimeId, otherRunner, cancellationToken);
            var ownerRelease = await gate.ReleaseAsync(runtimeId, owner!, cancellationToken);

            await Assert.That(wrongOwnerRelease)
                .IsEqualTo(RunnerCapacityReleaseOutcome.OwnerMismatch);
            await Assert.That(ownerRelease).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            await AssertCapacityAsync(database, owner!, memory: 1024, nanoCpus: 100, pids: 10);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Heartbeat_loss_rejects_new_claims_but_allows_existing_claim_release(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var database = redis.GetDatabase();
            const string pool = "integration";
            const string runner = "runner-offline";
            await RegisterRunnerAsync(database, pool, runner);
            var gate = new RedisRunnerCapacityGate(redis);
            var claimedRuntimeId = Guid.CreateVersion7();
            var claimed = await gate.TryClaimForRunnerAsync(
                new RunnerCapacityRequest(claimedRuntimeId, pool, 256, 25, 2),
                runner,
                cancellationToken);
            await Assert.That(claimed.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await database.KeyDeleteAsync($"runner:{runner}:heartbeat");

            var replay = await gate.TryClaimAsync(
                new RunnerCapacityRequest(claimedRuntimeId, pool, 256, 25, 2),
                cancellationToken);
            var rejected = await gate.TryClaimForRunnerAsync(
                new RunnerCapacityRequest(Guid.CreateVersion7(), pool, 256, 25, 2),
                runner,
                cancellationToken);
            var released = await gate.ReleaseAsync(claimedRuntimeId, runner, cancellationToken);

            await Assert.That(replay.Availability).IsEqualTo(RunnerCapacityAvailability.Insufficient);
            await Assert.That(replay.Failure).IsEqualTo(RunnerAdmissionFailure.NoEligibleRunner);
            await Assert.That(rejected.Availability)
                .IsEqualTo(RunnerCapacityAvailability.Insufficient);
            await Assert.That(released).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            await AssertCapacityAsync(database, runner, memory: 1024, nanoCpus: 100, pids: 10);
        });
    }

    private static async Task RegisterRunnerAsync(
        IDatabase database,
        string pool,
        string runner)
    {
        await database.SetAddAsync($"runner-pool:{pool}:members", runner);
        await database.StringSetAsync(
            $"runner:{runner}:heartbeat",
            "alive",
            TimeSpan.FromMinutes(1));
        await database.HashSetAsync($"runner:{runner}:capacity",
        [
            new("availableMemoryBytes", 1024),
            new("availableNanoCpus", 100),
            new("availablePids", 10),
            new("totalMemoryBytes", 1024),
            new("totalNanoCpus", 100),
            new("totalPids", 10)
        ]);
    }

    private static async Task AssertCapacityAsync(
        IDatabase database,
        string runner,
        long memory,
        long nanoCpus,
        long pids)
    {
        var capacity = await database.HashGetAsync(
            $"runner:{runner}:capacity",
            ["availableMemoryBytes", "availableNanoCpus", "availablePids"]);
        await Assert.That((long)capacity[0]!).IsEqualTo(memory);
        await Assert.That((long)capacity[1]!).IsEqualTo(nanoCpus);
        await Assert.That((long)capacity[2]!).IsEqualTo(pids);
    }

    private static HashEntry[] Capacity(long memory) =>
    [
        new("availableMemoryBytes", memory),
        new("availableNanoCpus", 100),
        new("availablePids", 10)
    ];
}
