using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RunnerAdmissionGateTests
{
    [Test, Timeout(300_000)]
    public async Task Startup_reservation_releases_only_after_an_observation_at_or_after_completion(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var options = new RunnerAdmissionOptions();
            var observedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            var sample = new RunnerResourceObservation("domain", observedAt, 1024, 1024, 100, 0, 0, 100, 0);
            RunnerAvailabilityRegistration Registration(DateTimeOffset at) => new("test", "runner", RuntimeProvider.Docker,
                "test", TimeSpan.FromMinutes(1), true, true,
                new RunnerPressurePolicy(options).Evaluate(sample with { ObservedAt = at }, DateTimeOffset.UtcNow),
                options);
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            await registry.RegisterAsync(Registration(observedAt) with { HasActiveAssignments = false }, ct);
            var gate = new RedisRunnerCapacityGate(redis);
            var id = Guid.NewGuid();
            var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.Runtime, id, id);
            await gate.TryClaimAsync(new(id, "test", 128, 10, 1, identity, Limit: new(128, 10, 1)), ct);
            await gate.CompleteWorkloadStartupAsync(identity, "runner", ct);

            await registry.RegisterAsync(Registration(observedAt), ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync(
                "runner:runner:capacity", "startupReservedMemoryBytes")).IsEqualTo(128);

            await Task.Delay(10, ct);
            await registry.RegisterAsync(Registration(DateTimeOffset.UtcNow), ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync(
                "runner:runner:capacity", "startupReservedMemoryBytes")).IsEqualTo(0);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync(
                "runner:runner:capacity", "startingPrimary")).IsEqualTo(0);
        });
    }

    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Oversized_reason_requires_evidence_for_every_pool_node(bool allTooSmall, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var policy = new RunnerAdmissionOptions();
            foreach (var runner in new[] { "small", "other" })
            {
                var blocked = runner == "other" && !allTooSmall;
                var now = DateTimeOffset.UtcNow;
                var sample = new RunnerResourceObservation(runner, now,
                    blocked ? 1024 : 128, blocked ? 900 : 120, 100, blocked ? 1 : .1, 1, 100, 0,
                    ProviderPressure: blocked);
                var admission = new RunnerPressurePolicy(policy).Evaluate(sample, now);
                await registry.RegisterAsync(new("test", runner, RuntimeProvider.Docker, "test",
                    TimeSpan.FromMinutes(1), false, true,
                    admission, policy), ct);
            }
            var id = Guid.NewGuid();
            var claim = await new RedisRunnerCapacityGate(redis).TryClaimAsync(new(id, "test", 256, 5, 1,
                new(RuntimeWorkloadKind.Runtime, id, id)), ct);
            await Assert.That(claim.Failure).IsEqualTo(allTooSmall
                ? RunnerAdmissionFailure.RequestExceedsNodeCapacity : RunnerAdmissionFailure.NodePressureHigh);
        });
    }

    [Test, Arguments(16), Arguments(64), Timeout(300_000)]
    public async Task Atomic_admission_limits_starts_and_reserves_checker_capacity(int parallelism, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var now = DateTimeOffset.UtcNow;
            var policy = new RunnerAdmissionOptions();
            var sample = new RunnerResourceObservation("domain", now, 1024, 900, 100, .1, 1, 100, 0);
            var registration = new RunnerAvailabilityRegistration("test", "runner", RuntimeProvider.Docker,
                "test", TimeSpan.FromMinutes(1), false, true,
                new RunnerPressurePolicy(policy).Evaluate(sample, now), policy);
            await registry.RegisterAsync(registration, ct);
            var gate = new RedisRunnerCapacityGate(redis);
            var requests = Enumerable.Range(0, parallelism).Select(_ =>
            {
                var id = Guid.NewGuid();
                return new RunnerCapacityRequest(id, "test", 64, 5, 1, new(RuntimeWorkloadKind.Runtime, id, id));
            }).ToArray();
            var claims = await Task.WhenAll(requests.Select(request => gate.TryClaimAsync(request, ct)));
            await Assert.That(claims.Count(claim => claim.Availability == RunnerCapacityAvailability.Claimed)).IsEqualTo(2);
            await Assert.That(claims.Where(claim => claim.Availability != RunnerCapacityAvailability.Claimed)
                .All(claim => claim.Failure == RunnerAdmissionFailure.StartupConcurrencyLimited)).IsTrue();
            foreach (var (request, claim) in requests.Zip(claims))
                if (claim.Availability == RunnerCapacityAvailability.Claimed)
                    await gate.CompleteWorkloadStartupAsync(request.Workload!.Value, "runner", ct);
            await Task.Delay(10, ct);
            var refreshedAt = DateTimeOffset.UtcNow;
            var refreshed = sample with { ObservedAt = refreshedAt };
            await registry.RegisterAsync(registration with
            {
                HasActiveAssignments = true,
                Admission = new RunnerPressurePolicy(policy).Evaluate(refreshed, refreshedAt)
            }, ct);
            var parent = Guid.NewGuid();
            var checker = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.AwdChecker, parent, Guid.NewGuid());
            var checkerClaim = await gate.TryClaimForRunnerAsync(new(parent, "test", 128, 10, 1, checker), "runner", ct);
            await Assert.That(checkerClaim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await registry.RegisterAsync(registration with
            {
                HasActiveAssignments = true,
                Admission = new RunnerAdmissionSnapshot(RunnerAdmissionState.Starting,
                    RunnerAdmissionFailure.ObservationStale,
                    sample with { ObservedAt = now.AddMinutes(-1) })
            }, ct);
            var staleId = Guid.NewGuid();
            var stale = await gate.TryClaimAsync(new(staleId, "test", 64, 5, 1,
                new(RuntimeWorkloadKind.Runtime, staleId, staleId)), ct);
            await Assert.That(stale.Failure).IsEqualTo(RunnerAdmissionFailure.ObservationStale);
            // Stale observations never prevent confirmed cleanup from returning a claim.
            await Assert.That(await gate.ReleaseWorkloadAsync(checker, "runner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            foreach (var (request, claim) in requests.Zip(claims))
                if (claim.Availability == RunnerCapacityAvailability.Claimed)
                    await gate.ReleaseWorkloadAsync(request.Workload!.Value, "runner", ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "startupReservedMemoryBytes"))
                .IsEqualTo(0);
        });
    }
}
