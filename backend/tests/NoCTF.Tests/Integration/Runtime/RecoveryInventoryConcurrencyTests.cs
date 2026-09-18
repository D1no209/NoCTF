using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Tests.Integration.Persistence;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class RecoveryInventoryConcurrencyTests
{
    [Test, Arguments(16), Arguments(64), Timeout(300_000)]
    public async Task Inventory_scan_does_not_hold_the_global_database_lock_while_other_nodes_allocate(int concurrency, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var cache = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), cache.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(cache.GetConnectionString());
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var setup = new NoCtfDbContext(options);
            await setup.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(setup, ct);
            var ids = Enumerable.Range(0, concurrency).Select(_ => Guid.NewGuid()).ToArray();
            foreach (var id in ids)
            {
                setup.Challenges.Add(new Challenge { Id = id, OwnerId = fixture.OwnerId, Mode = GameMode.Ctf,
                    Title = "Concurrent allocation", DefinitionJson = "{\"schemaVersion\":3}", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
                setup.RuntimeInstances.Add(new RuntimeInstance { Id = id, ChallengeId = id, Purpose = RuntimePurpose.TemplateTest,
                    RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, State = RuntimeState.Queued,
                    TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired, CreatedAt = fixture.Now });
            }
            await setup.SaveChangesAsync(ct);
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            await registry.RegisterAsync(new("test", "other", RuntimeProvider.Docker, "test", new(10000, 10000, 10000), TimeSpan.FromMinutes(2), false), ct);
            var batch = redis.GetDatabase().CreateBatch();
            var seeds = Enumerable.Range(0, 8192).Select(_ => batch.HashSetAsync($"runner-claim:{Guid.NewGuid():N}",
                [new HashEntry("runnerId", "recovering"), new("memoryBytes", 1), new("nanoCpus", 1), new("pidsLimit", 1)])).ToArray();
            batch.Execute();
            await Task.WhenAll(seeds);
            var services = new ServiceCollection().AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            await using var provider = services.BuildServiceProvider();
            var inventory = Substitute.For<IRuntimeManagedResourceReconciler>();
            inventory.Provider.Returns(RuntimeProvider.Docker);
            inventory.ListManagedAsync(ct).Returns(Task.FromResult<IReadOnlyList<RuntimeResourceIdentity>>([]));
            using var publisher = new RunnerAvailabilityPublisher(provider.GetRequiredService<IServiceScopeFactory>(), registry,
                Options.Create(new RunnerOptions { Id = "recovering", Pool = "test", Provider = RuntimeProvider.Docker,
                    Capacity = new() { MemoryBytes = 10000, NanoCpus = 10000, PidsLimit = 10000 },
                    Heartbeat = new() { IntervalSeconds = 5, TtlSeconds = 120 } }),
                NullLogger<RunnerAvailabilityPublisher>.Instance, TimeProvider.System, ledger: new RedisRunnerCapacityLedger(redis),
                mutations: new RunnerResourceMutationCoordinator(), reconcilers: [inventory]);
            using var monitor = new Process { StartInfo = new ProcessStartInfo("docker")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            foreach (var argument in new[] { "exec", cache.Id, "redis-cli", "MONITOR" }) monitor.StartInfo.ArgumentList.Add(argument);
            monitor.Start();
            try
            {
                _ = await monitor.StandardOutput.ReadLineAsync(ct); // MONITOR acknowledgement before recovery begins.
                var scanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var readMonitor = Task.Run(async () =>
                {
                    try
                    {
                        while (await monitor.StandardOutput.ReadLineAsync(reading.Token) is { } line)
                            if (line.Contains("\"SCAN\"", StringComparison.OrdinalIgnoreCase)) scanning.TrySetResult();
                    }
                    catch (OperationCanceledException) when (reading.IsCancellationRequested) { }
                }, ct);
                var watch = Stopwatch.StartNew();
                var recovery = publisher.PublishOnceAsync(ct);
                if (await Task.WhenAny(recovery, scanning.Task) == recovery) await recovery;
                await scanning.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                double globalLockWaitMs;
                await using (var probe = new NoCtfDbContext(options))
                await using (var transaction = await probe.Database.BeginTransactionAsync(ct))
                {
                    var lockWait = Stopwatch.StartNew();
                    await RuntimeCapacityCriticalSection.AcquireAsync(probe, ct);
                    globalLockWaitMs = lockWait.Elapsed.TotalMilliseconds;
                    await Assert.That(recovery.IsCompleted).IsFalse();
                    await transaction.CommitAsync(ct);
                }
                var durations = new ConcurrentQueue<double>();
                var outbox = new Outbox();
                await Task.WhenAll(ids.Select(async id =>
                {
                    await using var db = new NoCtfDbContext(options);
                    var gate = new PersistedRunnerCapacityGate(db, new RedisRunnerCapacityGate(redis), outbox);
                    var started = Stopwatch.GetTimestamp();
                    await Assert.That((await gate.TryClaimForRunnerAsync(new(id, "test", 1, 1, 1), "other", ct)).Availability)
                        .IsEqualTo(RunnerCapacityAvailability.Claimed);
                    await gate.ReleaseAsync(id, "other", ct);
                    durations.Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }));
                await Assert.That(await recovery).IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
                var elapsed = watch.Elapsed.TotalMilliseconds;
                var raw = new RedisRunnerCapacityGate(redis);
                foreach (var release in outbox.Messages.OfType<ReleaseRunnerCapacity>())
                    await raw.ReleaseWorkloadAsync(release.Identity, release.RunnerId, ct);
                await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:other:capacity", "availableMemoryBytes")).IsEqualTo(10000);
                reading.Cancel();
                await readMonitor;
                var directory = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_MEASUREMENTS");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, $"inventory-concurrency-{concurrency}.json"),
                        System.Text.Json.JsonSerializer.Serialize(new { concurrency, inventoryClaims = 8192, recoveryAndConcurrentWorkMs = elapsed, globalLockWaitMs,
                            allocationAndReleaseMs = durations.ToArray(), databaseLockAcquiredDuringInventory = true }), ct);
                }
            }
            finally { if (!monitor.HasExited) monitor.Kill(entireProcessTree: true); }
        });
    }

    private sealed class Outbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Messages { get; } = new();
        public ValueTask PublishAsync<T>(T message) { Messages.Enqueue(message!); return ValueTask.CompletedTask; }
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
