using System.Diagnostics;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runtime.Docker.Containers;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

/// <summary>Bounded real-provider correctness load; timings are diagnostic, not a production latency benchmark.</summary>
[Category("Integration"), NotInParallel]
public sealed class RuntimeCapacityLoadTests
{
    [Test, Arguments(16, 1), Arguments(64, 1), Arguments(16, 2), Arguments(64, 2), Timeout(300_000)]
    public async Task Strict_and_shared_cpu_preserve_checker_reserve_and_recover_exactly(int concurrency, int factor, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await redisContainer.StartAsync(ct);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(redisContainer.GetConnectionString());
            await using var image = new ContainerBuilder("busybox:1.36.1").WithCommand("true").Build();
            await image.StartAsync(ct);
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            using var provider = new DockerContainerLifecycle(new(Endpoint: endpoint, NetworkName: "none"));
            var gate = new RedisRunnerCapacityGate(redis);
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            var ledger = new RedisRunnerCapacityLedger(redis);
            var total = new RuntimeResourceLimits(1024L * 1024 * 1024, 850_000_000, 2048);
            var limit = new RuntimeResourceLimits(64L * 1024 * 1024, 200_000_000, 64);
            var budget = new RuntimeResourceBudgetPolicy(factor).Calculate(limit, RuntimeProvider.Docker);
            var admitted = new List<RuntimeCapacityAllocation>();
            var receipts = new List<ContainerReceipt>();
            var metrics = new List<object>();
            var createMs = new List<double>();
            var stopMs = new List<double>();
            var claimMs = new List<double>();
            var recoveryMs = new List<double>();
            var queueMs = new List<double>();
            async Task RegisterAsync() => await registry.RegisterAsync(new("load", "runner", RuntimeProvider.Docker,
                "test", total, TimeSpan.FromMinutes(2), admitted.Count > 0, true,
                new(RunnerAdmissionState.Ready, null, new("controlled-test", DateTimeOffset.UtcNow,
                    total.MemoryBytes, total.MemoryBytes, total.NanoCpus, 0, 1, total.PidsLimit, 0)), new()), ct);
            try
            {
                foreach (var phase in new[] { "idle", "burst", "full" })
                {
                    await RegisterAsync();
                    var requests = Enumerable.Range(0, concurrency).Select(_ =>
                    {
                        var id = Guid.NewGuid();
                        return new RunnerCapacityRequest(id, "load", budget.MemoryBytes, budget.NanoCpus, budget.PidsLimit,
                            new(RuntimeWorkloadKind.Runtime, id, id), Limit: new(limit.MemoryBytes, limit.NanoCpus, limit.PidsLimit));
                    }).ToList();
                    var queuedAt = Stopwatch.GetTimestamp();
                    while (requests.Count > 0)
                    {
                        await RegisterAsync();
                        var started = Stopwatch.GetTimestamp();
                        var outcomes = await Task.WhenAll(requests.Select(request => gate.TryClaimAsync(request, ct)));
                        claimMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        var accepted = requests.Zip(outcomes).Where(pair => pair.Second.Availability == RunnerCapacityAvailability.Claimed)
                            .Select(pair => pair.First).ToArray();
                        await Assert.That(accepted.Length <= 2).IsTrue();
                        if (accepted.Length == 0) break;
                        foreach (var request in accepted)
                        {
                            var identity = request.Workload!.Value;
                            admitted.Add(new(identity, null, "controlled-test", "runner",
                                new(budget.MemoryBytes, budget.NanoCpus, budget.PidsLimit), request.Limit!));
                            queueMs.Add(Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
                            started = Stopwatch.GetTimestamp();
                            var command = phase == "idle" ? "sleep 120" : phase == "burst"
                                ? "dd if=/dev/zero of=/dev/null bs=4096 count=100000; sleep 120" : "while :; do :; done";
                            receipts.Add(await provider.CreateAsync(new(identity.OperationId, RuntimeProvider.Docker, "busybox:1.36.1",
                                ["sh", "-c", command], new Dictionary<string, string>(), new Dictionary<string, string>(),
                                new Dictionary<int, int>(), limit, new(true, false, false, ["ALL"], []), null, Budget: budget), ct));
                            createMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                            await gate.CompleteWorkloadStartupAsync(identity, "runner", ct);
                            requests.Remove(request);
                        }
                    }
                    await Assert.That(admitted.Count).IsEqualTo(factor == 1 ? 3 : 6);
                    // Long-lived workloads exhausted their CPU share, but the full strict checker still fits.
                    var parent = admitted[0].Identity.RuntimeInstanceId;
                    var checker = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.AwdChecker, parent, Guid.NewGuid());
                    var checkerAmount = new RuntimeResourceAmount(256L * 1024 * 1024, 250_000_000, 128);
                    await Assert.That((await gate.TryClaimForRunnerAsync(new(parent, "load", checkerAmount.MemoryBytes,
                        checkerAmount.NanoCpus, checkerAmount.PidsLimit, checker), "runner", ct)).Availability)
                        .IsEqualTo(RunnerCapacityAvailability.Claimed);
                    admitted.Add(new(checker, Guid.NewGuid(), "controlled-test", "runner", checkerAmount, checkerAmount));
                    var timer = Stopwatch.GetTimestamp();
                    receipts.Add(await provider.CreateAsync(new(checker.OperationId, RuntimeProvider.Docker, "busybox:1.36.1",
                        ["sleep", "120"], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int>(),
                        new(checkerAmount.MemoryBytes, checkerAmount.NanoCpus, checkerAmount.PidsLimit),
                        new(true, false, false, ["ALL"], []), null), ct));
                    createMs.Add(Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    // Simulate total loss of this isolated Redis database while all provider resources remain.
                    await redis.GetDatabase().ExecuteAsync("FLUSHDB");
                    timer = Stopwatch.GetTimestamp();
                    await ledger.PauseAsync("runner", "load", ct);
                    var keys = await ledger.ReadClaimKeysAsync("runner", ct);
                    await ledger.RestoreAsync("runner", "load", total, admitted, ct, claimKeys: keys);
                    await RegisterAsync();
                    recoveryMs.Add(Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
                    await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "availableNanoCpus")).IsEqualTo(0);
                    foreach (var receipt in receipts)
                    {
                        timer = Stopwatch.GetTimestamp();
                        await provider.DestroyAsync(receipt, ct);
                        stopMs.Add(Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
                    }
                    receipts.Clear();
                    foreach (var allocation in admitted)
                    {
                        await Assert.That(await gate.ReleaseWorkloadAsync(allocation.Identity, "runner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
                        await Assert.That(await gate.ReleaseWorkloadAsync(allocation.Identity, "runner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.AlreadyReleased);
                    }
                    admitted.Clear();
                    var balance = await redis.GetDatabase().HashGetAsync("runner:runner:capacity", ["availableMemoryBytes", "availableNanoCpus", "availablePids"]);
                    await Assert.That(balance.Select(value => (long)value).ToArray()).IsEquivalentTo(new[] { total.MemoryBytes, total.NanoCpus, total.PidsLimit });
                    metrics.Add(new { phase, queuedNotAdmitted = requests.Count });
                }
                var output = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_MEASUREMENTS");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    await File.WriteAllTextAsync(Path.Combine(output, $"capacity-{factor}-{concurrency}.json"), JsonSerializer.Serialize(new
                    {
                        factor, concurrency, observations = "controlled healthy snapshot; not host-pressure acceptance",
                        createMs, stopMs, claimBatchMs = claimMs, recoveryMs, queueMs, metrics,
                        redisCommands = redis.GetCounters().Interactive.OperationCount,
                        providerLifecycleCalls = createMs.Count + stopMs.Count,
                        databaseCalls = 0 // This component fixture restores supplied committed documents; PG is covered separately.
                    }, new JsonSerializerOptions { WriteIndented = true }), ct);
                }
            }
            finally
            {
                foreach (var receipt in receipts) await provider.DestroyAsync(receipt, CancellationToken.None);
            }
        });
    }
}
