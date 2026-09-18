using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Worker;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class DetachedCheckerReconciliationTests
{
    [Test, Timeout(300_000)]
    public async Task Fresh_auditor_cleans_a_checker_after_parent_owner_was_cleared(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var cache = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), cache.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(cache.GetConnectionString());
            var dbOptions = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            var runtimeId = fixture.RuntimeIds[0];
            var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.PatchChecker, runtimeId, Guid.NewGuid());
            await new RedisRunnerAvailabilityRegistry(redis).RegisterAsync(new("test", "runner", RuntimeProvider.Docker,
                "test", new(1024, 100, 10), TimeSpan.FromMinutes(1), false), ct);
            var raw = new RedisRunnerCapacityGate(redis);
            var outbox = new Outbox();
            await using (var original = new NoCtfDbContext(dbOptions))
            {
                await original.Database.EnsureCreatedAsync(ct);
                await fixture.SeedAsync(original, ct);
                var runtime = await original.RuntimeInstances.SingleAsync(row => row.Id == runtimeId, ct);
                runtime.State = RuntimeState.Running;
                runtime.RunnerId = "runner";
                runtime.StoppedAt = null;
                var fact = await original.GameplayFacts.SingleAsync(row => row.Id == runtime.GameplayFactId, ct);
                fact.State = GameplayFactState.Processing;
                await original.SaveChangesAsync(ct);
                var gate = new PersistedRunnerCapacityGate(original, raw, outbox);
                await Assert.That((await gate.TryClaimForRunnerAsync(new(runtimeId, "test", 128, 10, 1, identity, fact.Id), "runner", ct)).Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                runtime.State = RuntimeState.Stopped;
                runtime.RunnerId = null;
                await original.SaveChangesAsync(ct);
            }
            await using var checker = new ContainerBuilder("busybox:1.36.1")
                .WithName($"noctf-{identity.OperationId:N}").WithCommand("sleep", "180")
                .WithLabel("noctf.io/managed", "true").WithLabel("noctf.io/runtime-instance-id", runtimeId.ToString("D"))
                .WithLabel("noctf.io/operation-id", identity.OperationId.ToString("N")).Build();
            await checker.StartAsync(ct);
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            var options = new DockerRuntimeOptions(Endpoint: endpoint);
            using var inventory = new DockerRuntimeResourceReconciler(options, new DockerComposeRuntime(options));
            using var lifecycle = new DockerContainerLifecycle(options);
            var catalog = Substitute.For<IRuntimeProviderCatalog>();
            catalog.Containers(RuntimeProvider.Docker).Returns(lifecycle);
            await using var restarted = new NoCtfDbContext(dbOptions);
            var persisted = new PersistedRunnerCapacityGate(restarted, raw, outbox);
            var runnerOptions = Options.Create(new RunnerOptions { Id = "runner", Pool = "test", Provider = RuntimeProvider.Docker });
            var audit = new RuntimeResourceReconciliationHandler(restarted, [new ScopedInventory(inventory, runtimeId)], runnerOptions,
                persisted, catalog, new RunnerResourceMutationCoordinator());
            await Assert.That(await inventory.WorkloadExistsAsync(identity, ct)).IsTrue();
            await audit.Handle(new("runner", DateTimeOffset.UtcNow), ct);
            await Assert.That(await inventory.WorkloadExistsAsync(identity, ct)).IsFalse();
            await Assert.That((await restarted.RuntimeInstances.AsNoTracking().SingleAsync(row => row.Id == runtimeId, ct)).CapacityAllocations.Items).IsEmpty();
            var release = outbox.Messages.OfType<ReleaseRunnerCapacity>().Single();
            var releaseHandler = new ReleaseRunnerCapacityHandler(restarted, persisted, outbox, TimeProvider.System, raw);
            await releaseHandler.Handle(release, ct);
            await releaseHandler.Handle(release, ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "availableMemoryBytes")).IsEqualTo(1024);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "activeAuxiliary")).IsEqualTo(0);
        });
    }

    // Restrict real Docker inventory to this fixture; never clean another task's containers.
    private sealed class ScopedInventory(IRuntimeManagedResourceReconciler inner, Guid runtimeId) : IRuntimeManagedResourceReconciler
    {
        public RuntimeProvider Provider => RuntimeProvider.Docker;
        public Task<bool?> WorkloadExistsAsync(RuntimeWorkloadIdentity identity, CancellationToken ct) => inner.WorkloadExistsAsync(identity, ct);
        public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(CancellationToken ct) =>
            (await inner.ListManagedAsync(ct)).Where(item => item.RuntimeInstanceId == runtimeId).ToArray();
        public Task DestroyByIdentityAsync(RuntimeResourceIdentity identity, CancellationToken ct) => identity.RuntimeInstanceId == runtimeId
            ? inner.DestroyByIdentityAsync(identity, ct) : throw new InvalidOperationException("Resource outside test scope.");
    }

    private sealed class Outbox : ITransactionalMessageOutbox
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message) { Messages.Add(message!); return ValueTask.CompletedTask; }
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
