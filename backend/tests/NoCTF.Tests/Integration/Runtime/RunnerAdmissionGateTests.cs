using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RunnerAdmissionGateTests
{
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
            var policy = new RunnerAdmissionOptions
            {
                AuxiliaryReservedMemoryBytes = 128, AuxiliaryReservedNanoCpus = 10, AuxiliaryReservedPids = 1
            };
            var registration = new RunnerAvailabilityRegistration("test", "runner", RuntimeProvider.Docker,
                "test", new(1024, 100, 100), TimeSpan.FromMinutes(1), false, true,
                new(RunnerAdmissionState.Ready, null, new("domain", now, 1024, 900, 100, .1, 1, 100, 0)), policy);
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
            var parent = Guid.NewGuid();
            var checker = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.AwdChecker, parent, Guid.NewGuid());
            var checkerClaim = await gate.TryClaimForRunnerAsync(new(parent, "test", 128, 10, 1, checker), "runner", ct);
            await Assert.That(checkerClaim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await registry.RegisterAsync(registration with
            {
                HasActiveAssignments = true,
                Admission = registration.Admission! with { Observation = registration.Admission.Observation! with { ObservedAt = now.AddMinutes(-1) } }
            }, ct);
            var stale = await gate.TryClaimAsync(requests[0] with { RuntimeInstanceId = Guid.NewGuid() }, ct);
            await Assert.That(stale.Failure).IsEqualTo(RunnerAdmissionFailure.ObservationStale);
            // Stale observations never prevent confirmed cleanup from returning a claim.
            await Assert.That(await gate.ReleaseWorkloadAsync(checker, "runner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            foreach (var (request, claim) in requests.Zip(claims))
                if (claim.Availability == RunnerCapacityAvailability.Claimed)
                    await gate.ReleaseWorkloadAsync(request.Workload!.Value, "runner", ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "availableMemoryBytes"))
                .IsEqualTo(1024);
        });
    }
}
