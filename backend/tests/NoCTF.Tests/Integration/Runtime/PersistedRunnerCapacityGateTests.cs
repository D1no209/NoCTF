using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Tests.Integration.Persistence;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Microsoft.Extensions.Options;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NSubstitute;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class PersistedRunnerCapacityGateTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Background_audit_recovers_rolled_back_then_cancelled_claim_without_restart(bool commit, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var cache = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), cache.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(cache.GetConnectionString());
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
            await db.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var id = fixture.RuntimeIds[0];
            await db.RuntimeInstances.Where(row => row.Id == id).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.State, RuntimeState.Queued).SetProperty(row => row.StoppedAt, (DateTimeOffset?)null), ct);
            await new RedisRunnerAvailabilityRegistry(redis).RegisterAsync(new("test", "runner", RuntimeProvider.Docker,
                "test", new(1024, 100, 10), TimeSpan.FromMinutes(1), false), ct);
            var raw = new RedisRunnerCapacityGate(redis);
            var outbox = new CapturedOutbox();
            var gate = new PersistedRunnerCapacityGate(db, raw, outbox);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await Assert.That((await gate.TryClaimAsync(new(id, "test", 512, 40, 2), ct)).Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                if (commit) await transaction.CommitAsync(ct);
                else await transaction.RollbackAsync(ct);
            }
            db.ChangeTracker.Clear();
            await db.RuntimeInstances.Where(row => row.Id == id).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.State, RuntimeState.Stopped).SetProperty(row => row.RunnerId, (string?)null), ct);
            var inventory = Substitute.For<IRuntimeManagedResourceReconciler>();
            inventory.Provider.Returns(RuntimeProvider.Docker);
            inventory.ListManagedAsync(ct).Returns(Task.FromResult<IReadOnlyList<RuntimeResourceIdentity>>([]));
            inventory.WorkloadExistsAsync(Arg.Any<RuntimeWorkloadIdentity>(), ct).Returns(Task.FromResult<bool?>(null));
            var ledger = new RedisRunnerCapacityLedger(redis);
            var audit = new RuntimeResourceReconciliationHandler(db, [inventory], Options.Create(new RunnerOptions
            {
                Id = "runner", Pool = "test", Provider = RuntimeProvider.Docker
            }), gate, ledger: ledger, rawCapacity: raw, outbox: outbox);
            await audit.Handle(new("runner", DateTimeOffset.UtcNow), ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "availableMemoryBytes")).IsEqualTo(512);
            inventory.WorkloadExistsAsync(Arg.Any<RuntimeWorkloadIdentity>(), ct).Returns(Task.FromResult<bool?>(false));
            await audit.Handle(new("runner", DateTimeOffset.UtcNow), ct);
            await audit.Handle(new("runner", DateTimeOffset.UtcNow), ct);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "availableMemoryBytes")).IsEqualTo(commit ? 512 : 1024);
            await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "startingPrimary")).IsEqualTo(commit ? 1 : 0);
            await Assert.That(await ledger.ReadUnconfirmedAsync("runner", ct)).IsEmpty();
            await Assert.That(outbox.Messages.OfType<DispatchQueuedRuntimes>().Count()).IsEqualTo(commit ? 0 : 1);
        });
    }

    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Database_commit_decides_which_claims_survive_Redis_reconstruction(bool rollback, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var redisContainer = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), redisContainer.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(redisContainer.GetConnectionString());
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            await fixture.SeedAsync(db, ct);
            var id = fixture.RuntimeIds[0];
            await db.RuntimeInstances.Where(x => x.Id == id).ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.State, RuntimeState.Queued).SetProperty(x => x.StoppedAt, (DateTimeOffset?)null), ct);
            var now = DateTimeOffset.UtcNow;
            var admissionOptions = new RunnerAdmissionOptions();
            var admission = new RunnerPressurePolicy(admissionOptions).Evaluate(
                new("test-domain", now, 1024, 1024, 100, .1, 0, 10, 0), now);
            var registration = new RunnerAvailabilityRegistration("test", "runner", RuntimeProvider.Docker,
                "test", new(0, 0, 0), TimeSpan.FromMinutes(1), false, Admission: admission,
                AdmissionOptions: admissionOptions, ActualUsage: true);
            var registry = new RedisRunnerAvailabilityRegistry(redis);
            await registry.RegisterAsync(registration, ct);
            var raw = new RedisRunnerCapacityGate(redis);
            var outbox = new CapturedOutbox();
            var gate = new PersistedRunnerCapacityGate(db, raw, outbox);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                var claim = await gate.TryClaimAsync(new(id, "test", 512, 40, 2), ct);
                await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
                if (rollback) await transaction.RollbackAsync(ct);
                else await transaction.CommitAsync(ct);
            }
            db.ChangeTracker.Clear();
            var document = await db.RuntimeInstances.Where(x => x.Id == id).Select(x => x.CapacityAllocations).SingleAsync(ct);
            await Assert.That(document.Items.Count).IsEqualTo(rollback ? 0 : 1);
            await db.RuntimeInstances.Where(x => x.Id == id).ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.State, RuntimeState.Provisioning).SetProperty(x => x.RunnerId, "runner"), ct);
            await Assert.That(await gate.CanCreateAsync(id, "runner", ct)).IsEqualTo(!rollback);
            var report = await new RedisRunnerCapacityDiagnostics(db, redis, TimeProvider.System).ReadAsync(ct);
            await Assert.That(report.Available).IsTrue();
            await Assert.That(report.Runners.Single().StartupReserved!.MemoryBytes).IsEqualTo(512);
            if (rollback) await Assert.That(report.Runners.Single().DeclaredLimits).IsNull();
            else await Assert.That(report.Runners.Single().DeclaredLimits!.MemoryBytes).IsEqualTo(512);
            // Redis failure occurs after allocation, before any provider command is consumed.
            await redis.GetDatabase().KeyDeleteAsync("runner:runner:capacity");
            await Assert.That(await gate.CanCreateAsync(id, "runner", ct)).IsFalse();
            var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.VerificationTarget, id, id);
            await Assert.That(await raw.ReleaseWorkloadAsync(identity, "runner", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.RecoveryRequired);
            var ledger = new RedisRunnerCapacityLedger(redis);
            await ledger.PauseAsync("runner", "test", ct);
            var claimKeys = await ledger.ReadClaimKeysAsync("runner", ct);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await RuntimeCapacityCriticalSection.AcquireAsync(db, ct);
                await ledger.RestoreAsync("runner", "test", admission.Capacity!, admission.Observation!.ObservedAt,
                    document.Items, ct,
                    starting: document.Items.Select(item => item.Identity).ToHashSet(), claimKeys: claimKeys);
                await transaction.CommitAsync(ct);
            }
            await registry.RegisterAsync(registration with { HasActiveAssignments = !rollback }, ct);
            var available = (long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "admissionAvailableMemoryBytes");
            await Assert.That(available).IsEqualTo(rollback ? 921 : 409);
            if (!rollback)
            {
                // Replays retain the committed budget, even when a caller's current policy differs.
                var replay = await gate.TryClaimAsync(new(id, "test", 1024, 100, 10), ct);
                await Assert.That(replay.State).IsEqualTo(RunnerCapacityClaimState.AlreadyOwned);
                var factId = await db.RuntimeInstances.Where(x => x.Id == id).Select(x => x.GameplayFactId).SingleAsync(ct);
                await db.GameplayFacts.Where(fact => fact.Id == factId).ExecuteUpdateAsync(update =>
                    update.SetProperty(fact => fact.State, NoCTF.Domain.Gameplay.GameplayFactState.Processing), ct);
                var checker = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.PatchChecker, id, Guid.NewGuid());
                var checkerRequest = new RunnerCapacityRequest(id, "test", 128, 10, 1, checker, factId);
                await db.RuntimeInstances.Where(row => row.Id == id).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.State, RuntimeState.Running).SetProperty(row => row.RunnerId, "runner"), ct);
                await Assert.That((await gate.TryClaimForRunnerAsync(checkerRequest, "wrong-owner", ct)).Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Unavailable);
                await Assert.That((await gate.TryClaimForRunnerAsync(checkerRequest, "runner", ct)).Availability)
                    .IsEqualTo(RunnerCapacityAvailability.Claimed);
                await Assert.That((await gate.TryClaimForRunnerAsync(checkerRequest, "runner", ct)).State)
                    .IsEqualTo(RunnerCapacityClaimState.AlreadyOwned);
                await Assert.That(await gate.CanCreateWorkloadAsync(checker, factId!.Value, "runner", ct)).IsTrue();
                await db.GameplayFacts.Where(fact => fact.Id == factId).ExecuteUpdateAsync(update =>
                    update.SetProperty(fact => fact.State, NoCTF.Domain.Gameplay.GameplayFactState.Completed), ct);
                await Assert.That(await gate.CanCreateWorkloadAsync(checker, factId.Value, "runner", ct)).IsFalse();
                await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "admissionAvailableMemoryBytes"))
                    .IsEqualTo(281);
                await Assert.That(await gate.ReleaseAsync(id, "wrong-owner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.OwnerMismatch);
                await Assert.That(await gate.ReleaseAsync(id, "runner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.Released);
                await Assert.That(await gate.ReleaseAsync(id, "runner", ct)).IsEqualTo(RunnerCapacityReleaseOutcome.AlreadyReleased);
                var retained = await db.RuntimeInstances.Where(x => x.Id == id).Select(x => x.CapacityAllocations).SingleAsync(ct);
                await Assert.That(retained.Items.Single().Identity).IsEqualTo(checker);
                await gate.ReleaseWorkloadAsync(checker, "runner", ct);
                foreach (var release in outbox.Messages.OfType<ReleaseRunnerCapacity>())
                {
                    await raw.ReleaseWorkloadAsync(release.Identity, release.RunnerId, ct);
                    await raw.ReleaseWorkloadAsync(release.Identity, release.RunnerId, ct);
                }
                await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "admissionAvailableMemoryBytes"))
                    .IsEqualTo(921);
                await Assert.That((long)await redis.GetDatabase().HashGetAsync("runner:runner:capacity", "startupReservedMemoryBytes"))
                    .IsEqualTo(0);
            }
        });
    }

    private sealed class CapturedOutbox : ITransactionalMessageOutbox
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message) { Messages.Add(message!); return ValueTask.CompletedTask; }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
