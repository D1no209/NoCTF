using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.Worker;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[Category("RunnerAssignmentReconciliation")]
public sealed class RunnerAssignmentReconciliationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Provision_writeback_rejects_unsafe_access_url_and_schedules_cleanup(
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
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisioned(
                        fixture.RedispatchId,
                        7,
                        1,
                        "runner-a",
                        RuntimeProvider.Docker,
                        "{\"resourceId\":\"unsafe-runtime\"}",
                        ["javascript:alert(1)"],
                        [0],
                        null),
                    db,
                    outbox,
                    cancellationToken);
            }

            await using var verify = new NoCtfDbContext(options);
            var runtime = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                item => item.Id == fixture.RedispatchId,
                cancellationToken);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(runtime.FailureCode).IsNull();
            await Assert.That(runtime.Urls).IsEmpty();
            await Assert.That(runtime.ParticipantUrlIndexes).IsEmpty();
            await Assert.That(JsonDocument.Parse(runtime.ProviderReceiptJson!).RootElement
                    .GetProperty("resourceId")
                    .GetString())
                .IsEqualTo("unsafe-runtime");
            var stop = outbox.RunnerNodeMessages.OfType<StopContainerRuntime>().Single();
            await Assert.That(stop.RuntimeInstanceId).IsEqualTo(fixture.RedispatchId);
            await Assert.That(stop.ProcessingVersion).IsEqualTo(8);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Offline_provisioning_without_receipt_routes_owner_cleanup_before_capacity_release(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var redisDatabase = redis.GetDatabase();
            const string pool = "pool-a";
            const string runner = "runner-a";
            await redisDatabase.SetAddAsync($"runner-pool:{pool}:members", runner);
            await redisDatabase.StringSetAsync(
                $"runner:{runner}:heartbeat",
                "alive",
                TimeSpan.FromMinutes(1));
            await redisDatabase.HashSetAsync(
                $"runner:{runner}:capacity",
                [
                    new("availableMemoryBytes", 1024),
                    new("availableNanoCpus", 100),
                    new("availablePids", 10),
                    new("totalMemoryBytes", 1024),
                    new("totalNanoCpus", 100),
                    new("totalPids", 10)
                ]);
            var capacity = new RedisRunnerCapacityGate(redis);
            var claim = await capacity.TryClaimForRunnerAsync(
                new RunnerCapacityRequest(fixture.RedispatchId, pool, 256, 25, 2),
                runner,
                cancellationToken);
            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await redisDatabase.KeyDeleteAsync($"runner:{runner}:heartbeat");

            var outbox = new RecordingTransactionalOutbox();
            await using (var db = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    db,
                    capacity,
                    outbox,
                    cancellationToken);
            }

            await using (var verify = new NoCtfDbContext(options))
            {
                var retained = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                await Assert.That(retained.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(retained.RunnerId).IsEqualTo(runner);
                await Assert.That(retained.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(retained.RunnerUnavailableAt).IsEqualTo(fixture.Now);
                await Assert.That(retained.ProcessingVersion).IsEqualTo(8);
            }
            await Assert.That(outbox.Published.OfType<ReleaseRunnerCapacity>()).IsEmpty();
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            var cleanup = outbox.RunnerNodeMessages.OfType<StopContainerRuntime>()
                .Single(message => message.RuntimeInstanceId == fixture.RedispatchId);
            await Assert.That(cleanup.RuntimeInstanceId).IsEqualTo(fixture.RedispatchId);
            await Assert.That(cleanup.ProcessingVersion).IsEqualTo(8);
            await Assert.That(cleanup.RunnerId).IsEqualTo(runner);
            await Assert.That(await redisDatabase.KeyExistsAsync(
                $"runner-claim:{fixture.RedispatchId:N}")).IsTrue();
            await Assert.That((long)(await redisDatabase.HashGetAsync(
                $"runner:{runner}:capacity",
                "availableMemoryBytes"))!).IsEqualTo(768);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Offline_running_without_receipt_cleans_exact_identity_before_releasing_and_dispatching_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var replacementId = Guid.CreateVersion7();
            await using (var prepare = new NoCtfDbContext(options))
            {
                var running = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                running.ProviderReceiptJson = null;
                await prepare.SaveChangesAsync(cancellationToken);
            }

            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var redisDatabase = redis.GetDatabase();
            const string pool = "pool-a";
            const string runner = "runner-c";
            foreach (var onlineRunner in new[] { "runner-a", "runner-b", runner })
            {
                await redisDatabase.SetAddAsync($"runner-pool:{pool}:members", onlineRunner);
                await redisDatabase.StringSetAsync(
                    $"runner:{onlineRunner}:heartbeat",
                    "alive",
                    TimeSpan.FromMinutes(1));
            }
            await redisDatabase.HashSetAsync(
                $"runner:{runner}:capacity",
                [
                    new("availableMemoryBytes", 1024),
                    new("availableNanoCpus", 100),
                    new("availablePids", 10),
                    new("totalMemoryBytes", 1024),
                    new("totalNanoCpus", 100),
                    new("totalPids", 10)
                ]);
            var capacity = new RedisRunnerCapacityGate(redis);
            var claim = await capacity.TryClaimForRunnerAsync(
                new RunnerCapacityRequest(fixture.RetainReceiptId, pool, 256, 25, 2),
                runner,
                cancellationToken);
            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await redisDatabase.KeyDeleteAsync($"runner:{runner}:heartbeat");

            var reconciliationOutbox = new RecordingTransactionalOutbox();
            await using (var reconcileDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    reconcileDb,
                    capacity,
                    reconciliationOutbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }
            await using (var replacementDb = new NoCtfDbContext(options))
            {
                var stopping = await replacementDb.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                replacementDb.RuntimeInstances.Add(CreateReplacement(
                    stopping,
                    replacementId,
                    fixture.Now.AddMinutes(4)));
                await replacementDb.SaveChangesAsync(cancellationToken);
            }

            var cleanup = reconciliationOutbox.RunnerNodeMessages
                .OfType<StopContainerRuntime>()
                .Single(message => message.RuntimeInstanceId == fixture.RetainReceiptId);
            await Assert.That(cleanup.Generation).IsEqualTo(1);
            await Assert.That(cleanup.RunnerPool).IsEqualTo(pool);
            await Assert.That(cleanup.RunnerId).IsEqualTo(runner);
            await Assert.That(reconciliationOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            await Assert.That(await redisDatabase.KeyExistsAsync(
                $"runner-claim:{fixture.RetainReceiptId:N}")).IsTrue();

            var services = new ServiceCollection();
            services.AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention());
            await using var serviceProvider = services.BuildServiceProvider();
            var workReader = new RuntimeNodeWorkReader(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());
            var reconciler = new RecordingResourceReconciler(RuntimeProvider.Docker, []);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = pool,
                    ["Runner:Id"] = runner
                })
                .Build();
            var handler = new RuntimeProviderHandler(
                new UnusedRuntimeProviderCatalog(),
                [reconciler],
                configuration,
                capacity,
                workReader);

            Func<Task> wrongOwner = async () =>
                _ = await handler.Handle(
                    cleanup with { RunnerId = "runner-b" },
                    cancellationToken);
            await Assert.That(wrongOwner).Throws<InvalidOperationException>();

            var wrongGeneration = (RuntimeStopped)await handler.Handle(
                cleanup with { Generation = 2 },
                cancellationToken);
            var fencedOutbox = new RecordingTransactionalOutbox();
            await using (var fencedDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    wrongGeneration,
                    fencedDb,
                    fencedOutbox,
                    cancellationToken);
            }
            await using (var wrongOwnerAckDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    wrongGeneration with { Generation = 1, RunnerId = "runner-b" },
                    wrongOwnerAckDb,
                    fencedOutbox,
                    cancellationToken);
            }
            await using (var fencedVerify = new NoCtfDbContext(options))
            {
                var waiting = await fencedVerify.RuntimeInstances.AsNoTracking().SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                await Assert.That(waiting.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(waiting.ProcessingVersion).IsEqualTo(8);
            }
            await Assert.That(fencedOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            await Assert.That(await redisDatabase.KeyExistsAsync(
                $"runner-claim:{fixture.RetainReceiptId:N}")).IsTrue();

            var firstAck = (RuntimeStopped)await handler.Handle(cleanup, cancellationToken);
            var duplicateAck = (RuntimeStopped)await handler.Handle(cleanup, cancellationToken);
            await Assert.That(reconciler.Destroyed)
                .IsEquivalentTo([
                    new RuntimeResourceIdentity(fixture.RetainReceiptId, 1),
                    new RuntimeResourceIdentity(fixture.RetainReceiptId, 1)
                ]);
            await Assert.That(await redisDatabase.KeyExistsAsync(
                $"runner-claim:{fixture.RetainReceiptId:N}")).IsFalse();
            await Assert.That((long)(await redisDatabase.HashGetAsync(
                $"runner:{runner}:capacity",
                "availableMemoryBytes"))!).IsEqualTo(1024);
            await Assert.That(reconciliationOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using (var completeDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    firstAck,
                    completeDb,
                    reconciliationOutbox,
                    cancellationToken);
            }
            await using (var replayDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    duplicateAck,
                    replayDb,
                    reconciliationOutbox,
                    cancellationToken);
            }

            var dispatches = reconciliationOutbox.Published.OfType<DispatchRuntime>()
                .Where(message => message.RuntimeInstanceId == replacementId)
                .ToArray();
            await Assert.That(dispatches).Count().IsEqualTo(1);
            await using var verify = new NoCtfDbContext(options);
            var stopped = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.RetainReceiptId,
                cancellationToken);
            await Assert.That(stopped.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopped.ProcessingVersion).IsEqualTo(9);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_unreceipted_cleanup_retains_capacity_and_later_start_retries_before_dispatch(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var failedReplacementId = Guid.CreateVersion7();
            await using (var prepare = new NoCtfDbContext(options))
            {
                var running = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                running.ProviderReceiptJson = null;
                await prepare.SaveChangesAsync(cancellationToken);
            }

            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var redisDatabase = redis.GetDatabase();
            const string pool = "pool-a";
            const string runner = "runner-c";
            foreach (var onlineRunner in new[] { "runner-a", "runner-b", runner })
            {
                await redisDatabase.SetAddAsync($"runner-pool:{pool}:members", onlineRunner);
                await redisDatabase.StringSetAsync(
                    $"runner:{onlineRunner}:heartbeat",
                    "alive",
                    TimeSpan.FromMinutes(1));
            }
            await redisDatabase.HashSetAsync(
                $"runner:{runner}:capacity",
                [
                    new("availableMemoryBytes", 1024),
                    new("availableNanoCpus", 100),
                    new("availablePids", 10),
                    new("totalMemoryBytes", 1024),
                    new("totalNanoCpus", 100),
                    new("totalPids", 10)
                ]);
            var capacity = new RedisRunnerCapacityGate(redis);
            var claim = await capacity.TryClaimForRunnerAsync(
                new RunnerCapacityRequest(fixture.RetainReceiptId, pool, 256, 25, 2),
                runner,
                cancellationToken);
            await Assert.That(claim.Availability).IsEqualTo(RunnerCapacityAvailability.Claimed);
            await redisDatabase.KeyDeleteAsync($"runner:{runner}:heartbeat");

            var reconciliationOutbox = new RecordingTransactionalOutbox();
            await using (var reconcileDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    reconcileDb,
                    capacity,
                    reconciliationOutbox,
                    cancellationToken);
            }
            await using (var replacementDb = new NoCtfDbContext(options))
            {
                var stopping = await replacementDb.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                replacementDb.RuntimeInstances.Add(CreateReplacement(
                    stopping,
                    failedReplacementId,
                    fixture.Now.AddMinutes(4)));
                await replacementDb.SaveChangesAsync(cancellationToken);
            }
            var initialCleanup = reconciliationOutbox.RunnerNodeMessages
                .OfType<StopContainerRuntime>()
                .Single(message => message.RuntimeInstanceId == fixture.RetainReceiptId);

            var services = new ServiceCollection();
            services.AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention());
            await using var serviceProvider = services.BuildServiceProvider();
            var workReader = new RuntimeNodeWorkReader(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = pool,
                    ["Runner:Id"] = runner
                })
                .Build();
            var failedHandler = new RuntimeProviderHandler(
                new UnusedRuntimeProviderCatalog(),
                [new RecordingResourceReconciler(
                    RuntimeProvider.Docker,
                    [],
                    failCleanup: true)],
                configuration,
                capacity,
                workReader);
            var failure = (RuntimeStopFailed)await failedHandler.Handle(
                initialCleanup,
                cancellationToken);
            await Assert.That(failure.Generation).IsEqualTo(1);
            await Assert.That(failure.RunnerId).IsEqualTo(runner);
            await using (var failureDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(failure, failureDb, cancellationToken);

            await Assert.That(await redisDatabase.KeyExistsAsync(
                $"runner-claim:{fixture.RetainReceiptId:N}")).IsTrue();
            await Assert.That((long)(await redisDatabase.HashGetAsync(
                $"runner:{runner}:capacity",
                "availableMemoryBytes"))!).IsEqualTo(768);
            await using (var failedVerify = new NoCtfDbContext(options))
            {
                var failed = await failedVerify.RuntimeInstances.AsNoTracking()
                    .Where(instance => instance.Id == fixture.RetainReceiptId
                        || instance.Id == failedReplacementId)
                    .OrderBy(instance => instance.Generation)
                    .ToListAsync(cancellationToken);
                await Assert.That(failed).Count().IsEqualTo(2);
                await Assert.That(failed.All(instance =>
                    instance.State == RuntimeState.Failed
                    && instance.FailureCode == RuntimeFailureCode.CleanupFailed)).IsTrue();
                await Assert.That(failed[0].RunnerId).IsEqualTo(runner);
            }

            var retryOutbox = new RecordingTransactionalOutbox();
            RuntimeMutationResult retry;
            await using (var retryDb = new NoCtfDbContext(options))
            {
                var target = await retryDb.RuntimeInstances.AsNoTracking()
                    .Where(instance => instance.Id == fixture.RetainReceiptId)
                    .Select(instance => new
                    {
                        instance.CompetitionId,
                        instance.CompetitionChallengeId
                    })
                    .SingleAsync(cancellationToken);
                var userId = await retryDb.Competitions.AsNoTracking()
                    .Where(competition => competition.Id == target.CompetitionId)
                    .Select(competition => competition.OwnerId)
                    .SingleAsync(cancellationToken);
                var store = new RuntimeInstanceStore(
                    retryDb,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(runnerPool: pool),
                    new PostgresPerTeamRuntimeFlagStore(retryDb),
                    retryOutbox);
                retry = await store.MutatePlayerRuntimeAsync(
                    new RuntimeMutationCommand(
                        target.CompetitionId,
                        target.CompetitionChallengeId,
                        userId,
                        RuntimeAction.Start,
                        null,
                        fixture.Now.AddMinutes(5)),
                    cancellationToken);
            }
            await Assert.That(retry.Failure).IsNull();
            await Assert.That(retry.Runtime).IsNotNull();
            await Assert.That(retry.Runtime!.Generation).IsEqualTo(3);
            await Assert.That(retryOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            var retryStop = retryOutbox.Published.OfType<StopRuntime>().Single();

            await using (var routeDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(
                    retryStop,
                    routeDb,
                    retryOutbox,
                    cancellationToken);
            }
            var retriedCleanup = retryOutbox.RunnerNodeMessages
                .OfType<StopContainerRuntime>()
                .Single(message => message.RuntimeInstanceId == fixture.RetainReceiptId);
            await Assert.That(retriedCleanup.Generation).IsEqualTo(1);
            await Assert.That(retriedCleanup.RunnerId).IsEqualTo(runner);

            var successfulReconciler = new RecordingResourceReconciler(
                RuntimeProvider.Docker,
                []);
            var successHandler = new RuntimeProviderHandler(
                new UnusedRuntimeProviderCatalog(),
                [successfulReconciler],
                configuration,
                capacity,
                workReader);
            var acknowledgement = (RuntimeStopped)await successHandler.Handle(
                retriedCleanup,
                cancellationToken);
            await Assert.That(successfulReconciler.Destroyed)
                .IsEquivalentTo([new RuntimeResourceIdentity(fixture.RetainReceiptId, 1)]);
            await Assert.That(await redisDatabase.KeyExistsAsync(
                $"runner-claim:{fixture.RetainReceiptId:N}")).IsFalse();
            await Assert.That(retryOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using (var acknowledgementDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    acknowledgement,
                    acknowledgementDb,
                    retryOutbox,
                    cancellationToken);
            }
            var dispatch = retryOutbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(dispatch.RuntimeInstanceId).IsEqualTo(retry.Runtime.Id);
            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.RetainReceiptId,
                cancellationToken);
            await Assert.That(old.State).IsEqualTo(RuntimeState.Stopped);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Offline_assignments_route_receipted_and_unreceipted_resources_back_to_the_owner_runner(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var receiptReleaseToken = Guid.CreateVersion7();
            await using (var prepare = new NoCtfDbContext(options))
            {
                var retained = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                retained.RunnerAssignmentReleaseToken = receiptReleaseToken;
                retained.RunnerUnavailableAt = fixture.Now.AddMinutes(-1);
                await prepare.SaveChangesAsync(cancellationToken);
            }
            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Offline);
            var outbox = new RecordingTransactionalOutbox();
            await using (var guardedReleaseDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerCapacityReleaseAsync(
                    new ReleaseRunnerCapacity(
                        fixture.RetainReceiptId,
                        7,
                        "pool-a",
                        "runner-c",
                        receiptReleaseToken),
                    guardedReleaseDb,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Superseded);
            }
            await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();

            await using (var db = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    db,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            await using (var verify = new NoCtfDbContext(options))
            {
                var provisioning = await verify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
                await Assert.That(provisioning.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(provisioning.RunnerId).IsEqualTo("runner-a");
                await Assert.That(provisioning.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(provisioning.RunnerUnavailableAt).IsEqualTo(fixture.Now);
                await Assert.That(provisioning.ProcessingVersion).IsEqualTo(8);

                var pendingStop = await verify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.CompleteStopId, cancellationToken);
                await Assert.That(pendingStop.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(pendingStop.StoppedAt).IsNull();
                await Assert.That(pendingStop.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(pendingStop.RunnerUnavailableAt).IsEqualTo(fixture.Now);
                await Assert.That(pendingStop.ProcessingVersion).IsEqualTo(8);

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
                await Assert.That(retained.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(retained.ProcessingVersion).IsEqualTo(8);
            }

            await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
            await Assert.That(outbox.Published.OfType<ReleaseRunnerCapacity>()).IsEmpty();
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            var cleanup = outbox.RunnerNodeMessages.OfType<StopContainerRuntime>().ToArray();
            await Assert.That(cleanup.Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([
                    fixture.RedispatchId,
                    fixture.CompleteStopId,
                    fixture.RetainReceiptId
                ]);
            await Assert.That(cleanup.All(message => message.ProcessingVersion == 8)).IsTrue();

            await using (var retainedVerify = new NoCtfDbContext(options))
            {
                var provisioning = await retainedVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
                await Assert.That(provisioning.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(provisioning.RunnerId).IsEqualTo("runner-a");
                await Assert.That(provisioning.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(provisioning.ProcessingVersion).IsEqualTo(8);

                var waitingForOwner = await retainedVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.CompleteStopId, cancellationToken);
                await Assert.That(waitingForOwner.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(waitingForOwner.StoppedAt).IsNull();
                await Assert.That(waitingForOwner.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(waitingForOwner.ProcessingVersion).IsEqualTo(8);
            }

            await using (var duplicateDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now.AddSeconds(1)),
                    duplicateDb,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Idempotent);
            }
            await using var duplicateVerify = new NoCtfDbContext(options);
            var duplicateRetained = await duplicateVerify.RuntimeInstances.AsNoTracking()
                .SingleAsync(instance => instance.Id == fixture.RetainReceiptId, cancellationToken);
            await Assert.That(duplicateRetained.ProcessingVersion).IsEqualTo(8);
            await Assert.That(outbox.RunnerNodeMessages.OfType<StopContainerRuntime>().Count()).IsEqualTo(3);
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
                    new ReconcileRunnerAssignments(fixture.Now),
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
            await Assert.That(outbox.Published).IsEmpty();
            await Assert.That(outbox.RunnerNodeMessages).IsEmpty();
            var retry = outbox.Scheduled.Single();
            await Assert.That(retry.Message).IsTypeOf<ReconcileRunnerAssignments>();
            await Assert.That(retry.At).IsGreaterThan(DateTimeOffset.UtcNow.AddSeconds(3));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Exactly_full_assignment_page_publishes_one_bounded_continuation(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var seedPage = new NoCtfDbContext(options))
            {
                var template = await seedPage.RuntimeInstances.AsNoTracking()
                    .OrderBy(instance => instance.Id)
                    .FirstAsync(cancellationToken);
                for (var index = 0; index < 497; index++)
                {
                    seedPage.RuntimeInstances.Add(new RuntimeInstance
                    {
                        Id = Guid.CreateVersion7(),
                        CompetitionId = template.CompetitionId,
                        CompetitionChallengeId = template.CompetitionChallengeId,
                        TeamId = null,
                        Purpose = RuntimePurpose.Player,
                        Generation = index + 2,
                        RuntimeKind = RuntimeKind.Container,
                        RuntimeProvider = RuntimeProvider.Docker,
                        RunnerId = "runner-page",
                        RunnerPool = "pool-a",
                        State = RuntimeState.Running,
                        ProcessingVersion = 1,
                        ProviderReceiptJson = "{}",
                        CreatedAt = fixture.Now.AddMinutes(index + 10)
                    });
                }
                await seedPage.SaveChangesAsync(cancellationToken);
            }

            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Online);
            var firstPageOutbox = new RecordingTransactionalOutbox();
            await using (var firstPageDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    firstPageDb,
                    capacity,
                    firstPageOutbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Idempotent);
            }

            var continuation = firstPageOutbox.Published
                .OfType<ReconcileRunnerAssignments>()
                .Single();
            await Assert.That(continuation.At).IsEqualTo(fixture.Now);
            await Assert.That(continuation.AfterRuntimeInstanceId).IsNotNull();
            await Assert.That(firstPageOutbox.Published).Count().IsEqualTo(1);
            await Assert.That(firstPageOutbox.RunnerNodeMessages).IsEmpty();

            var finalPageOutbox = new RecordingTransactionalOutbox();
            await using (var finalPageDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    continuation,
                    finalPageDb,
                    capacity,
                    finalPageOutbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Idempotent);
            }
            await Assert.That(finalPageOutbox.Published
                .OfType<ReconcileRunnerAssignments>()).IsEmpty();
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
                    new ReconcileRunnerAssignments(fixture.Now),
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
    public async Task Stop_invalidates_pending_release_and_original_provision_cleanup_dispatches_replacement(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Offline);
            var releaseToken = Guid.CreateVersion7();
            await using (var prepare = new NoCtfDbContext(options))
            {
                var runtime = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                runtime.RunnerAssignmentReleaseToken = releaseToken;
                runtime.ProcessingVersion = 8;
                await prepare.SaveChangesAsync(cancellationToken);
            }
            var release = new ReleaseRunnerCapacity(
                fixture.RedispatchId,
                8,
                "pool-a",
                "runner-a",
                releaseToken);
            var services = new ServiceCollection();
            services.AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention());
            await using var provider = services.BuildServiceProvider();
            var reader = new RuntimeNodeWorkReader(
                provider.GetRequiredService<IServiceScopeFactory>());
            var originalProvision = new ProvisionContainerRuntime(
                fixture.RedispatchId,
                7,
                1,
                "pool-a",
                "runner-a",
                null!);
            await Assert.That(await reader.ReadProvisionStatusAsync(
                    originalProvision,
                    cancellationToken))
                .IsEqualTo(RuntimeProvisionWorkStatus.AssignmentRetained);

            var replacementId = Guid.CreateVersion7();
            await using (var resetDb = new NoCtfDbContext(options))
            {
                var old = await resetDb.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                old.State = RuntimeState.Stopping;
                old.RunnerAssignmentReleaseToken = null;
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
            await Assert.That(await reader.ReadProvisionStatusAsync(
                    originalProvision,
                    cancellationToken))
                .IsEqualTo(RuntimeProvisionWorkStatus.StopRequested);

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
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Superseded);
            }
            await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
            await Assert.That(continuationOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using (var canceledDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionCanceled(
                        fixture.RedispatchId,
                        7,
                        1,
                        "pool-a",
                        "runner-a"),
                    canceledDb,
                    continuationOutbox,
                    cancellationToken);

            var replacementDispatch = continuationOutbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(replacementDispatch.RuntimeInstanceId).IsEqualTo(replacementId);
            await using var verify = new NoCtfDbContext(options);
            var stopped = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.RedispatchId,
                cancellationToken);
            await Assert.That(stopped.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopped.RunnerAssignmentReleaseToken).IsNull();
            await Assert.That(stopped.ProcessingVersion).IsEqualTo(10);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Offline_stopping_assignment_waits_for_late_receipt_cleanup_before_dispatching_replacement(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var resetOutbox = new RecordingTransactionalOutbox();
            Guid replacementId;
            await using (var resetDb = new NoCtfDbContext(options))
            {
                var target = await resetDb.RuntimeInstances.AsNoTracking()
                    .Where(instance => instance.Id == fixture.RedispatchId)
                    .Select(instance => new
                    {
                        instance.CompetitionId,
                        instance.CompetitionChallengeId
                    })
                    .SingleAsync(cancellationToken);
                var userId = await resetDb.Competitions.AsNoTracking()
                    .Where(competition => competition.Id == target.CompetitionId)
                    .Select(competition => competition.OwnerId)
                    .SingleAsync(cancellationToken);
                var store = new RuntimeInstanceStore(
                    resetDb,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(runnerPool: "pool-a"),
                    new PostgresPerTeamRuntimeFlagStore(resetDb),
                    resetOutbox);
                var reset = await store.MutatePlayerRuntimeAsync(
                    new RuntimeMutationCommand(
                        target.CompetitionId,
                        target.CompetitionChallengeId,
                        userId,
                        RuntimeAction.Reset,
                        null,
                        fixture.Now.AddSeconds(1)),
                    cancellationToken);
                await Assert.That(reset.Failure).IsNull();
                await Assert.That(reset.Runtime).IsNotNull();
                replacementId = reset.Runtime!.Id;
            }
            var stop = resetOutbox.Published.OfType<StopRuntime>().Single();
            await Assert.That(stop.RuntimeInstanceId).IsEqualTo(fixture.RedispatchId);
            await Assert.That(stop.ProcessingVersion).IsEqualTo(8);
            await Assert.That(resetOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            var capacity = new ReconciliationCapacityGate(runnerId =>
                runnerId == "runner-a"
                    ? RunnerHeartbeatStatus.Offline
                    : RunnerHeartbeatStatus.Online);
            var outbox = new RecordingTransactionalOutbox();
            await using (var reconcileDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    reconcileDb,
                    capacity,
                    outbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }
            await Assert.That(outbox.Published.OfType<ReleaseRunnerCapacity>()).IsEmpty();
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            await using (var pendingDb = new NoCtfDbContext(options))
            {
                var pending = await pendingDb.RuntimeInstances.AsNoTracking().SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                await Assert.That(pending.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(pending.StoppedAt).IsNull();
                await Assert.That(pending.RunnerAssignmentReleaseToken).IsNull();
                await Assert.That(pending.ProcessingVersion).IsEqualTo(9);
            }

            const string lateReceipt = """{"resourceId":"late-offline"}""";
            await using (var successDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisioned(
                        fixture.RedispatchId,
                        7,
                        1,
                        "runner-a",
                        RuntimeProvider.Docker,
                        lateReceipt,
                        [],
                        [],
                        null),
                    successDb,
                    outbox,
                    cancellationToken);

            var typedStops = outbox.RunnerNodeMessages.OfType<StopContainerRuntime>()
                .Where(message => message.RuntimeInstanceId == fixture.RedispatchId)
                .OrderBy(message => message.ProcessingVersion)
                .ToArray();
            await Assert.That(typedStops.Select(message => message.ProcessingVersion))
                .IsEquivalentTo([9L, 10L]);
            await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using (var completeDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopped(
                        fixture.RedispatchId,
                        10,
                        1,
                        "pool-a",
                        "runner-a"),
                    completeDb,
                    outbox,
                    cancellationToken);

            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(dispatch.RuntimeInstanceId).IsEqualTo(replacementId);
            await using var verify = new NoCtfDbContext(options);
            var stopped = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.RedispatchId,
                cancellationToken);
            await Assert.That(stopped.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopped.StoppedAt).IsNotNull();
            await Assert.That(stopped.RunnerAssignmentReleaseToken).IsNull();
            await Assert.That(stopped.ProcessingVersion).IsEqualTo(11);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == replacementId,
                cancellationToken);
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Queued);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Provision_cleanup_acknowledgements_are_exact_idempotent_and_dispatch_replacements(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var replacementId = Guid.CreateVersion7();
            var terminatedReplacementId = Guid.CreateVersion7();
            var outbox = new RecordingTransactionalOutbox();

            await using (var provisioningDb = new NoCtfDbContext(options))
            {
                var provisioning = await provisioningDb.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                provisioning.State = RuntimeState.Provisioning;
                provisioning.ProviderReceiptJson = null;
                await provisioningDb.SaveChangesAsync(cancellationToken);
            }
            var failed = new RuntimeProvisionTerminated(
                fixture.RetainReceiptId,
                7,
                1,
                "pool-a",
                "runner-c",
                RuntimeFailureCode.ProviderRejected);
            await using (var failedDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    failed,
                    failedDb,
                    outbox,
                    cancellationToken);
            await using (var failedReplayDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    failed,
                    failedReplayDb,
                    outbox,
                    cancellationToken);

            await using (var mismatchedFailureDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionFailed(
                        fixture.RedispatchId,
                        7,
                        RuntimeFailureCode.ProviderRejected,
                        "runner-z"),
                    mismatchedFailureDb,
                    cancellationToken);
            await using (var cleanupDb = new NoCtfDbContext(options))
            {
                var competitionId = await cleanupDb.RuntimeInstances.AsNoTracking()
                    .Where(instance => instance.Id == fixture.RedispatchId)
                    .Select(instance => instance.CompetitionId)
                    .SingleAsync(cancellationToken);
                await BackendMessageHandlers.Handle(
                    new CleanupCompetitionRuntimes(competitionId),
                    cleanupDb,
                    outbox,
                    cancellationToken);
            }
            var cleanupStop = outbox.Published.OfType<StopRuntime>()
                .Single(message => message.RuntimeInstanceId == fixture.RedispatchId);
            await Assert.That(cleanupStop.ProcessingVersion).IsEqualTo(8);

            await using (var prepare = new NoCtfDbContext(options))
            {
                var stopped = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                prepare.RuntimeInstances.Add(CreateReplacement(
                    stopped,
                    replacementId,
                    fixture.Now.AddMinutes(4)));
                var stopping = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.CompleteStopId,
                    cancellationToken);
                prepare.RuntimeInstances.Add(CreateReplacement(
                    stopping,
                    terminatedReplacementId,
                    fixture.Now.AddMinutes(5)));
                await prepare.SaveChangesAsync(cancellationToken);
            }

            var services = new ServiceCollection();
            services.AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention());
            await using var provider = services.BuildServiceProvider();
            var reader = new RuntimeNodeWorkReader(
                provider.GetRequiredService<IServiceScopeFactory>());
            var provisionMessage = new ProvisionContainerRuntime(
                fixture.RedispatchId,
                7,
                1,
                "pool-a",
                "runner-a",
                null!);
            var status = await reader.ReadProvisionStatusAsync(
                provisionMessage,
                cancellationToken);
            await Assert.That(status).IsEqualTo(RuntimeProvisionWorkStatus.StopRequested);

            await using (var stopDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(
                    new StopRuntime(fixture.RedispatchId, 8),
                    stopDb,
                    outbox,
                    cancellationToken);
            }

            await using (var stoppedTooEarly = new NoCtfDbContext(options))
            {
                var waiting = await stoppedTooEarly.RuntimeInstances.AsNoTracking()
                    .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
                await Assert.That(waiting.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(waiting.ProcessingVersion).IsEqualTo(8);
                await Assert.That(waiting.ProviderReceiptJson).IsNull();
                await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            }

            var invalidAcks = new[]
            {
                new RuntimeProvisionCanceled(
                    fixture.RedispatchId,
                    6,
                    1,
                    "pool-a",
                    "runner-a"),
                new RuntimeProvisionCanceled(
                    fixture.RedispatchId,
                    7,
                    2,
                    "pool-a",
                    "runner-a"),
                new RuntimeProvisionCanceled(
                    fixture.RedispatchId,
                    7,
                    1,
                    "pool-b",
                    "runner-a"),
                new RuntimeProvisionCanceled(
                    fixture.RedispatchId,
                    7,
                    1,
                    "pool-a",
                    "runner-b")
            };
            foreach (var invalidAck in invalidAcks)
            {
                await using var invalidDb = new NoCtfDbContext(options);
                await RuntimeWriteBackHandler.Handle(
                    invalidAck,
                    invalidDb,
                    outbox,
                    cancellationToken);
            }

            var canceled = new RuntimeProvisionCanceled(
                fixture.RedispatchId,
                7,
                1,
                "pool-a",
                "runner-a");
            await using (var canceledDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    canceled,
                    canceledDb,
                    outbox,
                    cancellationToken);
            await using (var replayDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    canceled,
                    replayDb,
                    outbox,
                    cancellationToken);

            var terminated = new RuntimeProvisionTerminated(
                fixture.CompleteStopId,
                6,
                1,
                "pool-a",
                "runner-b",
                RuntimeFailureCode.ProviderRejected);
            await using (var terminatedDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    terminated,
                    terminatedDb,
                    outbox,
                    cancellationToken);
            await using (var terminatedReplayDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    terminated,
                    terminatedReplayDb,
                    outbox,
                    cancellationToken);

            await using var verify = new NoCtfDbContext(options);
            var result = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
            await Assert.That(result.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(result.ProcessingVersion).IsEqualTo(9);
            await Assert.That(result.ProviderReceiptJson).IsNull();
            var terminatedResult = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(
                    instance => instance.Id == fixture.CompleteStopId,
                    cancellationToken);
            await Assert.That(terminatedResult.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(terminatedResult.ProcessingVersion).IsEqualTo(8);
            await Assert.That(terminatedResult.FailureCode).IsNull();
            var failedResult = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
            await Assert.That(failedResult.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(failedResult.ProcessingVersion).IsEqualTo(8);
            await Assert.That(failedResult.FailureCode)
                .IsEqualTo(RuntimeFailureCode.ProviderRejected);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()
                .Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([replacementId, terminatedReplacementId]);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Late_provision_success_persists_receipt_until_typed_stop_completes(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var replacementId = Guid.CreateVersion7();
            var outbox = new RecordingTransactionalOutbox();

            var invalidReplies = new[]
            {
                new RuntimeProvisioned(
                    fixture.RedispatchId,
                    7,
                    2,
                    "runner-a",
                    RuntimeProvider.Docker,
                    "{}",
                    [],
                    [],
                    null),
                new RuntimeProvisioned(
                    fixture.RedispatchId,
                    7,
                    1,
                    "runner-z",
                    RuntimeProvider.Docker,
                    "{}",
                    [],
                    [],
                    null),
                new RuntimeProvisioned(
                    fixture.RedispatchId,
                    7,
                    1,
                    "runner-a",
                    RuntimeProvider.Libvirt,
                    "{}",
                    [],
                    [],
                    null)
            };
            foreach (var invalidReply in invalidReplies)
            {
                await using var invalidDb = new NoCtfDbContext(options);
                await RuntimeWriteBackHandler.Handle(
                    invalidReply,
                    invalidDb,
                    outbox,
                    cancellationToken);
            }
            await using (var invalidVerify = new NoCtfDbContext(options))
            {
                var unchanged = await invalidVerify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(
                        instance => instance.Id == fixture.RedispatchId,
                        cancellationToken);
                await Assert.That(unchanged.State).IsEqualTo(RuntimeState.Provisioning);
                await Assert.That(unchanged.ProcessingVersion).IsEqualTo(7);
                await Assert.That(unchanged.ProviderReceiptJson).IsNull();
            }

            await using (var cleanupDb = new NoCtfDbContext(options))
            {
                var competitionId = await cleanupDb.RuntimeInstances.AsNoTracking()
                    .Where(instance => instance.Id == fixture.RedispatchId)
                    .Select(instance => instance.CompetitionId)
                    .SingleAsync(cancellationToken);
                await BackendMessageHandlers.Handle(
                    new CleanupCompetitionRuntimes(competitionId),
                    cleanupDb,
                    outbox,
                    cancellationToken);
            }
            await using (var prepare = new NoCtfDbContext(options))
            {
                var stopped = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RedispatchId,
                    cancellationToken);
                prepare.RuntimeInstances.Add(CreateReplacement(
                    stopped,
                    replacementId,
                    fixture.Now.AddMinutes(4)));
                await prepare.SaveChangesAsync(cancellationToken);
            }

            var services = new ServiceCollection();
            services.AddDbContext<NoCtfDbContext>(builder => builder
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention());
            await using var provider = services.BuildServiceProvider();
            var reader = new RuntimeNodeWorkReader(
                provider.GetRequiredService<IServiceScopeFactory>());
            var provisionMessage = new ProvisionContainerRuntime(
                fixture.RedispatchId,
                7,
                1,
                "pool-a",
                "runner-a",
                null!);
            var status = await reader.ReadProvisionStatusAsync(
                provisionMessage,
                cancellationToken);
            await Assert.That(status).IsEqualTo(RuntimeProvisionWorkStatus.StopRequested);

            await using (var stopDb = new NoCtfDbContext(options))
                await BackendMessageHandlers.Handle(
                    new StopRuntime(fixture.RedispatchId, 8),
                    stopDb,
                    outbox,
                    cancellationToken);

            const string lateReceipt = """{"resourceId":"late"}""";
            await using (var successDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisioned(
                        fixture.RedispatchId,
                        7,
                        1,
                        "runner-a",
                        RuntimeProvider.Docker,
                        lateReceipt,
                        [],
                        [],
                        null),
                    successDb,
                    outbox,
                    cancellationToken);

            status = await reader.ReadProvisionStatusAsync(provisionMessage, cancellationToken);
            await Assert.That(status).IsEqualTo(RuntimeProvisionWorkStatus.AssignmentRetained);
            var typedStops = outbox.RunnerNodeMessages.OfType<StopContainerRuntime>()
                .OrderBy(message => message.ProcessingVersion)
                .ToArray();
            await Assert.That(typedStops.Select(message => message.ProcessingVersion))
                .IsEquivalentTo([8L, 9L]);
            var directStop = typedStops[^1];
            await Assert.That(directStop.RuntimeInstanceId).IsEqualTo(fixture.RedispatchId);
            await Assert.That(directStop.ProcessingVersion).IsEqualTo(9);
            await Assert.That(directStop.Generation).IsEqualTo(1);
            await Assert.That(directStop.RunnerId).IsEqualTo("runner-a");

            await using (var receiptFenceDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionCanceled(
                        fixture.RedispatchId,
                        7,
                        1,
                        "pool-a",
                        "runner-a"),
                    receiptFenceDb,
                    outbox,
                    cancellationToken);
            await using (var terminatedReceiptFenceDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionTerminated(
                        fixture.RedispatchId,
                        7,
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.ProviderRejected),
                    terminatedReceiptFenceDb,
                    outbox,
                    cancellationToken);
            await using (var staleFailureDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionFailed(
                        fixture.RedispatchId,
                        7,
                        RuntimeFailureCode.RunnerUnavailable,
                        "runner-a"),
                    staleFailureDb,
                    cancellationToken);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using (var completeDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopped(
                        fixture.RedispatchId,
                        9,
                        1,
                        "pool-a",
                        "runner-a"),
                    completeDb,
                    outbox,
                    cancellationToken);

            await using var verify = new NoCtfDbContext(options);
            var result = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(instance => instance.Id == fixture.RedispatchId, cancellationToken);
            await Assert.That(result.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(result.ProcessingVersion).IsEqualTo(10);
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(result.ProviderReceiptJson!),
                JsonNode.Parse(lateReceipt))).IsTrue();
            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(dispatch.RuntimeInstanceId).IsEqualTo(replacementId);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_cleanup_owner_is_audited_offline_but_not_when_heartbeat_is_unavailable(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var prepare = new NoCtfDbContext(options))
            {
                var failed = await prepare.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                failed.State = RuntimeState.Failed;
                failed.FailureCode = RuntimeFailureCode.CleanupFailed;
                await prepare.SaveChangesAsync(cancellationToken);
            }

            var unavailableOutbox = new RecordingTransactionalOutbox();
            var inventory = new Func<string, RunnerPoolInventory>(pool => new(
                RunnerPoolInventoryAvailability.Available,
                pool == "pool-a" ? ["runner-c"] : []));
            await using (var unavailableDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now),
                    unavailableDb,
                    new ReconciliationCapacityGate(
                        runnerId => runnerId == "runner-c"
                            ? RunnerHeartbeatStatus.Unavailable
                            : RunnerHeartbeatStatus.Online,
                        inventory),
                    unavailableOutbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.DeferredCapacity);
            }
            await Assert.That(unavailableOutbox.RunnerNodeMessages).IsEmpty();

            var offlineOutbox = new RecordingTransactionalOutbox();
            await using (var offlineDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(fixture.Now.AddSeconds(1)),
                    offlineDb,
                    new ReconciliationCapacityGate(
                        runnerId => runnerId == "runner-c"
                            ? RunnerHeartbeatStatus.Offline
                            : RunnerHeartbeatStatus.Online,
                        inventory),
                    offlineOutbox,
                    cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }
            var audit = offlineOutbox.RunnerNodeMessages
                .OfType<ReconcileRuntimeResources>()
                .Single();
            await Assert.That(audit.RunnerPool).IsEqualTo("pool-a");
            await Assert.That(audit.RunnerId).IsEqualTo("runner-c");
            await Assert.That(audit.RequestedAt).IsEqualTo(fixture.Now.AddSeconds(1));
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
                    new ReconcileRunnerAssignments(fixture.Now),
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
            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Online);
            await using var db = new NoCtfDbContext(options);
            var handler = new RuntimeResourceReconciliationHandler(
                db,
                [provider],
                configuration,
                capacity);

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
            await Assert.That(capacity.ReleasedRuntimeIds).IsEquivalentTo([orphanId]);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Successful_failed_assignment_cleanup_commits_before_unrelated_orphan_failure(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var releaseToken = Guid.CreateVersion7();
            await using (var seed = new NoCtfDbContext(options))
            {
                var runtime = await seed.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                runtime.State = RuntimeState.Failed;
                runtime.FailureCode = RuntimeFailureCode.CleanupFailed;
                runtime.RunnerAssignmentReleaseToken = releaseToken;
                runtime.RunnerUnavailableAt = fixture.Now;
                await seed.SaveChangesAsync(cancellationToken);
            }

            var orphan = new RuntimeResourceIdentity(Guid.CreateVersion7(), 1);
            var reconciler = new RecordingResourceReconciler(
                RuntimeProvider.Docker,
                [orphan],
                failedIdentity: orphan);
            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Online);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = "pool-a",
                    ["Runner:Id"] = "runner-c",
                    ["Runner:Provider"] = nameof(RuntimeProvider.Docker)
                })
                .Build();
            var message = new ReconcileRuntimeResources(
                "pool-a",
                "runner-c",
                fixture.Now);
            await using (var db = new NoCtfDbContext(options))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    db,
                    [reconciler],
                    configuration,
                    capacity);
                Func<Task> action = () => handler.Handle(message, cancellationToken);
                await Assert.That(action).Throws<InvalidOperationException>();
            }
            await using (var replayDb = new NoCtfDbContext(options))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    replayDb,
                    [reconciler],
                    configuration,
                    capacity);
                Func<Task> action = () => handler.Handle(message, cancellationToken);
                await Assert.That(action).Throws<InvalidOperationException>();
            }

            await Assert.That(reconciler.Destroyed)
                .IsEquivalentTo(
                [
                    new RuntimeResourceIdentity(fixture.RetainReceiptId, 1),
                    orphan,
                    orphan
                ]);
            await Assert.That(capacity.ReleasedRuntimeIds)
                .IsEquivalentTo([fixture.RetainReceiptId]);
            await using var verify = new NoCtfDbContext(options);
            var cleaned = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
            await Assert.That(cleaned.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(cleaned.FailureCode)
                .IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(cleaned.ProviderReceiptJson).IsNull();
            await Assert.That(cleaned.RunnerId).IsNull();
            await Assert.That(cleaned.RunnerAssignmentReleaseToken).IsNull();
            await Assert.That(cleaned.RunnerUnavailableAt).IsNull();
            await Assert.That(cleaned.ProcessingVersion).IsEqualTo(8);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_receipt_reconciliation_retries_until_cleanup_and_capacity_release_converge(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var options = CreateOptions(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, cancellationToken);
            var releaseToken = Guid.CreateVersion7();
            await using (var seed = new NoCtfDbContext(options))
            {
                var runtime = await seed.RuntimeInstances.SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
                runtime.RuntimeKind = RuntimeKind.OvaVm;
                runtime.RuntimeProvider = RuntimeProvider.Libvirt;
                runtime.RunnerId = "runner-a";
                runtime.State = RuntimeState.Failed;
                runtime.FailureCode = RuntimeFailureCode.CleanupFailed;
                runtime.RunnerAssignmentReleaseToken = releaseToken;
                runtime.RunnerUnavailableAt = fixture.Now;
                await seed.SaveChangesAsync(cancellationToken);
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Runner:Pool"] = "pool-a",
                    ["Runner:Id"] = "runner-a",
                    ["Runner:Provider"] = nameof(RuntimeProvider.Libvirt)
                })
                .Build();
            var cleanupFailure = new RecordingResourceReconciler(
                RuntimeProvider.Libvirt,
                [],
                failCleanup: true);
            var cleanupCapacity = new ReconciliationCapacityGate(
                _ => RunnerHeartbeatStatus.Online);
            await using (var cleanupDb = new NoCtfDbContext(options))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    cleanupDb,
                    [cleanupFailure],
                    configuration,
                    cleanupCapacity);
                Func<Task> action = () => handler.Handle(
                    new ReconcileRuntimeResources("pool-a", "runner-a", fixture.Now),
                    cancellationToken);
                await Assert.That(action).Throws<InvalidOperationException>();
            }
            await Assert.That(cleanupCapacity.ReleasedRuntimeIds).IsEmpty();

            var reconciler = new RecordingResourceReconciler(RuntimeProvider.Libvirt, []);
            var mismatchedCapacity = new ReconciliationCapacityGate(
                _ => RunnerHeartbeatStatus.Online,
                release: (_, _) => RunnerCapacityReleaseOutcome.OwnerMismatch);
            await using (var mismatchDb = new NoCtfDbContext(options))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    mismatchDb,
                    [reconciler],
                    configuration,
                    mismatchedCapacity);
                Func<Task> action = () => handler.Handle(
                    new ReconcileRuntimeResources("pool-a", "runner-a", fixture.Now),
                    cancellationToken);
                await Assert.That(action).Throws<InvalidOperationException>();
            }
            await using (var unchangedDb = new NoCtfDbContext(options))
            {
                var unchanged = await unchangedDb.RuntimeInstances.AsNoTracking()
                    .SingleAsync(
                        instance => instance.Id == fixture.RetainReceiptId,
                        cancellationToken);
                await Assert.That(unchanged.State).IsEqualTo(RuntimeState.Failed);
                await Assert.That(unchanged.FailureCode)
                    .IsEqualTo(RuntimeFailureCode.CleanupFailed);
                await Assert.That(unchanged.ProviderReceiptJson).IsNotNull();
                await Assert.That(unchanged.RunnerId).IsEqualTo("runner-a");
                await Assert.That(unchanged.RunnerAssignmentReleaseToken)
                    .IsEqualTo(releaseToken);
                await Assert.That(unchanged.RunnerUnavailableAt).IsEqualTo(fixture.Now);
                await Assert.That(unchanged.ProcessingVersion).IsEqualTo(7);
            }

            var capacity = new ReconciliationCapacityGate(_ => RunnerHeartbeatStatus.Online);
            var message = new ReconcileRuntimeResources("pool-a", "runner-a", fixture.Now);
            await using (var convergeDb = new NoCtfDbContext(options))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    convergeDb,
                    [reconciler],
                    configuration,
                    capacity);
                await handler.Handle(message, cancellationToken);
            }
            await using (var replayDb = new NoCtfDbContext(options))
            {
                var handler = new RuntimeResourceReconciliationHandler(
                    replayDb,
                    [reconciler],
                    configuration,
                    capacity);
                await handler.Handle(message, cancellationToken);
            }

            await Assert.That(reconciler.Destroyed)
                .IsEquivalentTo(
                [
                    new RuntimeResourceIdentity(fixture.RetainReceiptId, 1),
                    new RuntimeResourceIdentity(fixture.RetainReceiptId, 1)
                ]);
            await Assert.That(capacity.ReleasedRuntimeIds)
                .IsEquivalentTo([fixture.RetainReceiptId]);
            await using var verify = new NoCtfDbContext(options);
            var converged = await verify.RuntimeInstances.AsNoTracking()
                .SingleAsync(
                    instance => instance.Id == fixture.RetainReceiptId,
                    cancellationToken);
            await Assert.That(converged.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(converged.FailureCode)
                .IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(converged.ProviderReceiptJson).IsNull();
            await Assert.That(converged.RunnerId).IsNull();
            await Assert.That(converged.RunnerAssignmentReleaseToken).IsNull();
            await Assert.That(converged.RunnerUnavailableAt).IsNull();
            await Assert.That(converged.ProcessingVersion).IsEqualTo(8);
        });
    }

    private static PostgreSqlContainer CreatePostgres() =>
        new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("noctf_runner_reconciliation")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static DbContextOptions<NoCtfDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    private static RuntimeInstance CreateReplacement(
        RuntimeInstance replaced,
        Guid replacementId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = replacementId,
            CompetitionId = replaced.CompetitionId,
            CompetitionChallengeId = replaced.CompetitionChallengeId,
            TeamId = replaced.TeamId,
            Generation = checked(replaced.Generation + 1),
            RuntimeKind = replaced.RuntimeKind,
            RuntimeProvider = replaced.RuntimeProvider,
            RunnerPool = replaced.RunnerPool,
            State = RuntimeState.Queued,
            ReplacesRuntimeInstanceId = replaced.Id,
            CreatedAt = createdAt
        };

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
        var teamId = Guid.CreateVersion7();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/reconciliation:v1"),
            new RuntimeResourceLimits(67_108_864, 100_000_000, 64));
        var definitionJson = JsonSerializer.Serialize(
            new CtfChallengeConfiguration(
                CtfChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                Runtime: runtime),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Reconciliation Team",
            NormalizedName = "RECONCILIATION TEAM",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = "0123456789abcdef0123456789abcdef",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
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
                Mode = GameMode.Ctf,
                Title = $"Challenge {index}",
                DefinitionJson = definitionJson,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                Order = index,
                IsPublished = true,
                UpdatedAt = now
            });
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = runtimeIds[index],
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                Purpose = RuntimePurpose.Player,
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
        Func<string, RunnerPoolInventory>? inventory = null,
        Func<Guid, string, RunnerCapacityReleaseOutcome>? release = null) : IRunnerCapacityGate
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
            return Task.FromResult(release?.Invoke(runtimeInstanceId, runnerId)
                ?? RunnerCapacityReleaseOutcome.Released);
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
        IReadOnlyList<RuntimeResourceIdentity> managed,
        bool failCleanup = false,
        RuntimeResourceIdentity? failedIdentity = null)
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
            return failCleanup || identity == failedIdentity
                ? Task.FromException(new InvalidOperationException("cleanup failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class UnusedRuntimeProviderCatalog : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IComposeRuntime Compose(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new NotSupportedException();
    }

}
