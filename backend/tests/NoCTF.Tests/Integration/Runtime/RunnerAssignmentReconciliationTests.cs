using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RunnerAssignmentReconciliationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Offline_assignments_recover_without_stealing_node_local_receipts(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Offline);
            var outbox = new RecordingTransactionalOutbox();

            await using (var db = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now, 1),
                    db,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            await using (var verify = new NoCtfDbContext(options))
            {
                var redispatched = await verify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
                await Assert.That(redispatched.State).IsEqualTo(RuntimeState.Provisioning);
                await Assert.That(redispatched.RunnerId).IsEqualTo("runner-a");
                await Assert.That(redispatched.RunnerAssignmentReleaseToken).IsNotNull();
                await Assert.That(redispatched.ProcessingVersion).IsEqualTo(8);

                var completed = await verify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.CompleteStopId, cancellationToken);
                await Assert.That(completed.State).IsEqualTo(RuntimeState.Stopped);
                await Assert.That(completed.StoppedAt).IsEqualTo(fixture.Now);
                await Assert.That(completed.ProcessingVersion).IsEqualTo(8);

                var retained = await verify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.RetainReceiptId, cancellationToken);
                await Assert.That(retained.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(retained.RunnerId).IsEqualTo("runner-c");
                await Assert.That(retained.ProviderReceiptJson).IsNotNull();
                await Assert.That(JsonNode.DeepEquals(
                    JsonNode.Parse(retained.ProviderReceiptJson!),
                    JsonNode.Parse("""{"id":"local"}"""))).IsTrue();
                await Assert.That(retained.FailureCode).IsNull();
                await Assert.That(retained.RunnerUnavailableAt).IsEqualTo(fixture.Now);
                await Assert.That(retained.ProcessingVersion).IsEqualTo(8);
            }

            await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
            var releases = outbox.Published.OfType<ReleaseRunnerCapacity>().ToArray();
            await Assert.That(releases.Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([fixture.RedispatchId, fixture.CompleteStopId]);
            var cleanup = outbox.RunnerNodeMessages.OfType<StopContainerRuntime>().Single();
            await Assert.That(cleanup.RuntimeInstanceId).IsEqualTo(fixture.RetainReceiptId);
            await Assert.That(cleanup.RunnerId).IsEqualTo("runner-c");
            await Assert.That(cleanup.ProcessingVersion).IsEqualTo(8);

            foreach (var release in releases)
            {
                await using var releaseDb = new NoCtfDbContext(options);
                var outcome = await BackendMessageHandlers.ExecuteRunnerCapacityReleaseAsync(
                    release,
                    releaseDb,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }
            await Assert.That(capacity.ReleasedRuntimeIds)
                .IsEquivalentTo([fixture.RedispatchId, fixture.CompleteStopId]);
            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(dispatch.RuntimeInstanceId).IsEqualTo(fixture.RedispatchId);
            await Assert.That(dispatch.ProcessingVersion).IsEqualTo(9);
            await using (var releasedVerify = new NoCtfDbContext(options))
            {
                var released = await releasedVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
                await Assert.That(released.State).IsEqualTo(RuntimeState.Queued);
                await Assert.That(released.RunnerId).IsNull();
                await Assert.That(released.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(released.ProcessingVersion).IsEqualTo(9);
            }

            await using (var duplicateDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now.AddSeconds(1), 1),
                    duplicateDb,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Superseded);
            }
            await using var duplicateVerify = new NoCtfDbContext(options);
            var duplicateRetained = await duplicateVerify.RuntimeInstances.AsNoTracking()
                .SingleAsync(instance => instance.Id == fixture.RetainReceiptId, cancellationToken);
            await Assert.That(duplicateRetained.ProcessingVersion).IsEqualTo(8);
            await Assert.That(outbox.RunnerNodeMessages.OfType<StopContainerRuntime>().Count()).IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Redis_unavailability_defers_without_changing_assignments(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingTransactionalOutbox();

            await using (var db = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now, 1),
                    db,
                    new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Unavailable),
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.DeferredCapacity);
            }

            await using var verify = new NoCtfDbContext(options);
            var unchanged = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
            await Assert.That(unchanged.State).IsEqualTo(RuntimeState.Provisioning);
            await Assert.That(unchanged.RunnerId).IsEqualTo("runner-a");
            await Assert.That(unchanged.ProcessingVersion).IsEqualTo(7);
            var schedule = await verify.DurableMaintenanceSchedules.AsNoTracking()
                .SingleAsync(
                    item => item.Kind == MaintenanceChainKind.RunnerAssignmentReconciliation,
                    cancellationToken);
            await Assert.That(schedule.ProcessingVersion).IsEqualTo(2);
            await Assert.That(outbox.Published).IsEmpty();
            await Assert.That(outbox.RunnerNodeMessages).IsEmpty();
            var retry = outbox.Scheduled.Single();
            await Assert.That(retry.Message).IsTypeOf<ReconcileRunnerAssignments>();
            await Assert.That(retry.At).IsGreaterThan(DateTimeOffset.UtcNow.AddSeconds(3));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Later_unavailable_heartbeat_aborts_before_any_capacity_release(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var capacity = new ReconciliationCapacityGate(runnerId => runnerId switch
            {
                "runner-a" => RunnerHeartbeatStatus.Offline,
                "runner-b" => RunnerHeartbeatStatus.Unavailable,
                _ => RunnerHeartbeatStatus.Online
            });
            var outbox = new RecordingTransactionalOutbox();

            await using (var db = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now, 1),
                    db,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.DeferredCapacity);
            }

            await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
            await Assert.That(outbox.Published).IsEmpty();
            await Assert.That(outbox.RunnerNodeMessages).IsEmpty();
            await using var verify = new NoCtfDbContext(options);
            var unchanged = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
            await Assert.That(unchanged.State).IsEqualTo(RuntimeState.Provisioning);
            await Assert.That(unchanged.RunnerId).IsEqualTo("runner-a");
            await Assert.That(unchanged.ProcessingVersion).IsEqualTo(7);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Reset_waits_for_pending_capacity_release_before_dispatching_replacement(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Offline);
            var reconciliationOutbox = new RecordingTransactionalOutbox();
            await using (var db = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now, 1),
                    db,
                    capacity,
                    reconciliationOutbox,
                    cancellationToken);
            }
            var release = reconciliationOutbox.Published.OfType<ReleaseRunnerCapacity>()
                .Single(message => message.RuntimeInstanceId == fixture.RedispatchId);
            var replacementId = Guid.CreateVersion7();
            await using (var resetDb = new NoCtfDbContext(options))
            {
                var old = await resetDb.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                old.State = RuntimeState.Stopping;
                old.ProcessingVersion = checked(old.ProcessingVersion + 1);
                resetDb.RuntimeInstances.Add(new RuntimeInstance
                {
                    Id = replacementId,
                    CompetitionId = old.CompetitionId,
                    CompetitionChallengeId = old.CompetitionChallengeId,
                    TeamId = old.TeamId,
                    Generation = checked(old.Generation + 1),
                    RuntimeKind = old.RuntimeKind,
                    RuntimeProvider = old.RuntimeProvider,
                    RunnerPool = old.RunnerPool,
                    State = RuntimeState.Queued,
                    ReplacesRuntimeInstanceId = old.Id,
                    CreatedAt = fixture.Now.AddSeconds(1)
                });
                await resetDb.SaveChangesAsync(cancellationToken);
            }

            var continuationOutbox = new RecordingTransactionalOutbox();
            await using (var stopDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(
                    new StopRuntime(fixture.RedispatchId, 9),
                    stopDb,
                    continuationOutbox,
                    cancellationToken);
            }
            await Assert.That(continuationOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using (var releaseDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerCapacityReleaseAsync(
                    release,
                    releaseDb,
                    capacity,
                    continuationOutbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }
            var replacementDispatch = continuationOutbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(replacementDispatch.RuntimeInstanceId).IsEqualTo(replacementId);
            await using var verify = new NoCtfDbContext(options);
            var stopped = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.RedispatchId,
                cancellationToken);
            await Assert.That(stopped.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopped.RunnerAssignmentReleaseToken).IsNull();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Online_pool_members_receive_exact_resource_reconciliation(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var seed = new NoCtfDbContext(options))
            {
                var runtime = await seed.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                runtime.RuntimeKind = RuntimeKind.OvaVm;
                runtime.RuntimeProvider = RuntimeProvider.Libvirt;
                await seed.SaveChangesAsync(cancellationToken);
            }
            var capacity = new ReconciliationCapacityGate(
                _ => RunnerHeartbeatStatus.Online,
                pool => new(
                    RunnerPoolInventoryAvailability.Available,
                    pool == "pool-a" ? ["runner-a"] : []));
            var outbox = new RecordingTransactionalOutbox();

            await using (var db = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now, 1),
                    db,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            var audit = outbox.RunnerNodeMessages
                .OfType<ReconcileRuntimeResources>()
                .Single();
            await Assert.That(audit.RunnerPool).IsEqualTo("pool-a");
            await Assert.That(audit.RunnerId).IsEqualTo("runner-a");
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Resource_reconciliation_preserves_only_the_current_node_assignment(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var seed = new NoCtfDbContext(options))
            {
                var runtime = await seed.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                runtime.RuntimeKind = RuntimeKind.OvaVm;
                runtime.RuntimeProvider = RuntimeProvider.Libvirt;
                await seed.SaveChangesAsync(cancellationToken);
            }

            var orphanId = Guid.CreateVersion7();
            var provider = new RecordingResourceReconciler(
                RuntimeProvider.Libvirt,
                [
                    new(fixture.RedispatchId, 1),
                    new(fixture.RedispatchId, 2),
                    new(orphanId, 1)
                ]);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = "pool-a",
                    ["Runner:Id"] = "runner-a",
                    ["Runner:Provider"] = nameof(RuntimeProvider.Libvirt)
                })
                .Build();
            await using var db = new NoCtfDbContext(options);
            var handler = new RuntimeResourceReconciliationHandler(
                db,
                [provider],
                configuration);

            await handler.Handle(
                new ReconcileRuntimeResources(
                    "pool-a",
                    "runner-a",
                    fixture.Now),
                cancellationToken);

            await Assert.That(provider.Destroyed)
                .IsEquivalentTo(
                [
                    new RuntimeResourceIdentity(fixture.RedispatchId, 2),
                    new RuntimeResourceIdentity(orphanId, 1)
                ]);
        });
    }

    private static PostgreSqlContainer CreatePostgres() =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_runner_reconciliation")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static DbContextOptions<NoCtfDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    private static async Task<ReconciliationFixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;
        var now = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Runner reconciliation",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });

        var runtimeIds = Enumerable.Range(0, 3)
            .Select(_ => Guid.CreateVersion7())
            .Order()
            .ToArray();
        var states = new[] { RuntimeState.Provisioning, RuntimeState.Stopping, RuntimeState.Running };
        for (var index = 0; index < runtimeIds.Length; index++)
        {
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Title = $"Challenge {index}",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                Order = index,
                UpdatedAt = now
            });
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = runtimeIds[index],
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                Generation = 1,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerId = $"runner-{(char)('a' + index)}",
                RunnerPool = "pool-a",
                State = states[index],
                ProcessingVersion = 7,
                ProviderReceiptJson = index == 2 ? "{\"id\":\"local\"}" : null,
                CreatedAt = now.AddMinutes(index)
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(now, runtimeIds[0], runtimeIds[1], runtimeIds[2]);
    }

    private sealed record ReconciliationFixture(
        DateTimeOffset Now,
        Guid RedispatchId,
        Guid CompleteStopId,
        Guid RetainReceiptId);

    private sealed class ReconciliationCapacityGate(
        Func<string, RunnerHeartbeatStatus> heartbeat,
        Func<string, RunnerPoolInventory>? inventory = null) : IRunnerCapacityGate
    {
        public List<Guid> ReleasedRuntimeIds { get; } = [];

        public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
            string runnerPool,
            string runnerId,
            CancellationToken cancellationToken) => Task.FromResult(heartbeat(runnerId));

        public Task<RunnerPoolInventory> GetPoolInventoryAsync(
            string runnerPool,
            CancellationToken cancellationToken) =>
            Task.FromResult(inventory?.Invoke(runnerPool)
                ?? new RunnerPoolInventory(
                    RunnerPoolInventoryAvailability.Available,
                    []));

        public Task<RunnerCapacityClaim> TryClaimAsync(
            RunnerCapacityRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
            RunnerCapacityRequest request,
            string runnerId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
            Guid runtimeInstanceId,
            string runnerId,
            CancellationToken cancellationToken)
        {
            ReleasedRuntimeIds.Add(runtimeInstanceId);
            return Task.FromResult(RunnerCapacityReleaseOutcome.Released);
        }
    }

    private sealed class RecordingTransactionalOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerNodeMessages { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            RunnerNodeMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage
        {
            RunnerNodeMessages.Add(message);
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class RecordingResourceReconciler(
        RuntimeProvider provider,
        IReadOnlyList<RuntimeResourceIdentity> managed)
        : IRuntimeManagedResourceReconciler
    {
        public RuntimeProvider Provider { get; } = provider;

        public List<RuntimeResourceIdentity> Destroyed { get; } = [];

        public Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(managed);

        public Task DestroyByIdentityAsync(
            RuntimeResourceIdentity identity,
            CancellationToken cancellationToken)
        {
            Destroyed.Add(identity);
            return Task.CompletedTask;
        }
    }

}
