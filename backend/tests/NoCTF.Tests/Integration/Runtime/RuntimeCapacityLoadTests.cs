using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class RuntimeCapacityLoadTests
{
    private static readonly RuntimeResourceAmount Limit = new(256L * 1024 * 1024, 500_000_000, 128);

    [Test, Timeout(300_000)]
    public async Task Three_idle_running_claims_do_not_block_a_fourth_start(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var gate = new RedisRunnerCapacityGate(redis);
            var policy = new RunnerAdmissionOptions();
            var publishCount = 0;

            async Task PublishFreshAsync()
            {
                await Task.Delay(5, ct);
                var now = DateTimeOffset.UtcNow;
                var observation = new RunnerResourceObservation("docker:production-equivalent", now,
                    8L * 1024 * 1024 * 1024, 7_500_000_000,
                    4_000_000_000, .05, 700, 4096, 0);
                var registration = new RunnerAvailabilityRegistration("production", "runner", RuntimeProvider.Docker,
                    "test", new(0, 0, 0), TimeSpan.FromMinutes(1), publishCount++ > 0, true,
                    new RunnerPressurePolicy(policy).Evaluate(observation, now), policy, ActualUsage: true);
                await Assert.That(await registry.RegisterAsync(registration, ct))
                    .IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            }

            await PublishFreshAsync();
            var running = new List<RuntimeWorkloadIdentity>();
            for (var index = 0; index < 3; index++)
            {
                var id = Guid.NewGuid();
                var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.Runtime, id, id);
                var claim = await gate.TryClaimAsync(new(id, "production", Limit.MemoryBytes,
                    Limit.NanoCpus, Limit.PidsLimit, identity, Limit: Limit), ct);
                await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
                await gate.CompleteWorkloadStartupAsync(identity, "runner", ct);
                await PublishFreshAsync();
                running.Add(identity);
            }

            var database = redis.GetDatabase();
            await Assert.That((long)await database.HashGetAsync(
                "runner:runner:capacity", "startupReservedNanoCpus")).IsEqualTo(0);
            foreach (var identity in running)
                await Assert.That(await database.KeyExistsAsync($"runner-claim:{identity.Key}")).IsTrue();

            var fourthId = Guid.NewGuid();
            var fourth = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.Runtime, fourthId, fourthId);
            var fourthClaim = await gate.TryClaimAsync(new(fourthId, "production", Limit.MemoryBytes,
                Limit.NanoCpus, Limit.PidsLimit, fourth, Limit: Limit), ct);

            await Assert.That(fourthClaim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await Assert.That((long)await database.HashGetAsync(
                "runner:runner:capacity", "startupReservedNanoCpus")).IsEqualTo(Limit.NanoCpus);
            await Assert.That((long)await database.HashGetAsync(
                "runner:runner:capacity", "startingPrimary")).IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Sixty_four_parallel_claims_are_atomic_and_never_make_capacity_negative(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var options = new RunnerAdmissionOptions { MainStartupConcurrency = 64 };
            var now = DateTimeOffset.UtcNow;
            var observation = new RunnerResourceObservation("docker:concurrency", now,
                4L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024,
                4_000_000_000, 0, 0, 4096, 0);
            var registration = new RunnerAvailabilityRegistration("parallel", "runner", RuntimeProvider.Docker,
                "test", new(0, 0, 0), TimeSpan.FromMinutes(1), false, true,
                new RunnerPressurePolicy(options).Evaluate(observation, now), options, ActualUsage: true);
            await new RedisRunnerAvailabilityRegistry(redis).RegisterAsync(registration, ct);
            var gate = new RedisRunnerCapacityGate(redis);
            var duplicateId = Guid.NewGuid();
            var duplicateIdentity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.Runtime, duplicateId, duplicateId);
            var duplicateRequest = new RunnerCapacityRequest(duplicateId, "parallel", Limit.MemoryBytes,
                Limit.NanoCpus, Limit.PidsLimit, duplicateIdentity, Limit: Limit);
            var duplicateClaims = await Task.WhenAll(Enumerable.Range(0, 64)
                .Select(_ => gate.TryClaimAsync(duplicateRequest, ct)));
            await Assert.That(duplicateClaims.All(claim => claim.Availability == RunnerCapacityAvailability.Claimed
                && claim.RunnerId == "runner")).IsTrue();
            await Assert.That((long)await redis.GetDatabase().HashGetAsync(
                "runner:runner:capacity", "startupReservedNanoCpus")).IsEqualTo(Limit.NanoCpus);
            await gate.ReleaseWorkloadAsync(duplicateIdentity, "runner", ct);

            var requests = Enumerable.Range(0, 64).Select(_ =>
            {
                var id = Guid.NewGuid();
                var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.Runtime, id, id);
                return new RunnerCapacityRequest(id, "parallel", Limit.MemoryBytes, Limit.NanoCpus,
                    Limit.PidsLimit, identity, Limit: Limit);
            }).ToArray();

            var claims = await Task.WhenAll(requests.Select(request => gate.TryClaimAsync(request, ct)));
            var admitted = claims.Count(claim => claim.Availability == RunnerCapacityAvailability.Claimed);
            await Assert.That(admitted).IsEqualTo(7);
            var fields = await redis.GetDatabase().HashGetAsync("runner:runner:capacity",
                ["admissionAvailableMemoryBytes", "admissionAvailableNanoCpus", "admissionAvailablePids"]);
            await Assert.That(fields.Select(value => (long)value).All(value => value >= 0)).IsTrue();
            await Assert.That(requests.Select(request => request.ClaimSuffix).Distinct().Count()).IsEqualTo(64);
        });
    }
}
