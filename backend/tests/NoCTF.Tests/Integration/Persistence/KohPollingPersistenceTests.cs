using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Koh;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Runner.Messages;
using NoCTF.Tests.Fixtures;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class KohPollingPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Provisioning_creates_one_shared_runtime_and_preserves_generations(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_runtime")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var cleanup = new NoCtfDbContext(options))
            {
                await cleanup.RuntimeInstances.ExecuteDeleteAsync(cancellationToken);
            }

            var firstOutbox = new RecordingOutbox();
            await using (var first = new NoCtfDbContext(options))
            {
                var outcome = await new PostgresKohRuntimeProvisioner(
                    first,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(),
                    firstOutbox,
                    new MutableTimeProvider(fixture.DueAt)).EnsureAsync(
                        fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(outcome).IsEqualTo(KohRuntimeProvisioningOutcome.Applied);
            }

            await Assert.That(firstOutbox.Published).Count().IsEqualTo(1);
            await Assert.That(firstOutbox.Published.Single()).IsTypeOf<DispatchRuntime>();
            await Assert.That(firstOutbox.FlushCount).IsEqualTo(1);
            await using (var verify = new NoCtfDbContext(options))
            {
                var runtime = await verify.RuntimeInstances.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(runtime.TeamId).IsNull();
                await Assert.That(runtime.Purpose).IsEqualTo(RuntimePurpose.Player);
                await Assert.That(runtime.State).IsEqualTo(RuntimeState.Queued);
                await Assert.That(runtime.Generation).IsEqualTo(1);
            }

            var replayOutbox = new RecordingOutbox();
            await using (var replay = new NoCtfDbContext(options))
            {
                var outcome = await new PostgresKohRuntimeProvisioner(
                    replay,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(),
                    replayOutbox,
                    new MutableTimeProvider(fixture.DueAt.AddSeconds(1))).EnsureAsync(
                        fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(outcome).IsEqualTo(KohRuntimeProvisioningOutcome.Idempotent);
                await Assert.That(await replay.RuntimeInstances.CountAsync(cancellationToken))
                    .IsEqualTo(1);
            }
            await Assert.That(replayOutbox.Published).IsEmpty();
            await Assert.That(replayOutbox.FlushCount).IsEqualTo(0);

            await using (var stopping = new NoCtfDbContext(options))
            {
                await stopping.RuntimeInstances.ExecuteUpdateAsync(
                    setters => setters.SetProperty(runtime => runtime.State, RuntimeState.Stopping),
                    cancellationToken);
            }
            var deferredOutbox = new RecordingOutbox();
            await using (var deferred = new NoCtfDbContext(options))
            {
                var outcome = await new PostgresKohRuntimeProvisioner(
                    deferred,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(),
                    deferredOutbox,
                    new MutableTimeProvider(fixture.DueAt.AddSeconds(2))).EnsureAsync(
                        fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(outcome).IsEqualTo(KohRuntimeProvisioningOutcome.DeferredCleanup);
                await Assert.That(await deferred.RuntimeInstances.CountAsync(cancellationToken))
                    .IsEqualTo(1);
            }
            await Assert.That(deferredOutbox.Published).IsEmpty();
            await Assert.That(deferredOutbox.FlushCount).IsEqualTo(0);
            await using (var stopped = new NoCtfDbContext(options))
            {
                await stopped.RuntimeInstances.ExecuteUpdateAsync(
                    setters => setters.SetProperty(runtime => runtime.State, RuntimeState.Stopped),
                    cancellationToken);
            }
            await using (var replacement = new NoCtfDbContext(options))
            {
                var outcome = await new PostgresKohRuntimeProvisioner(
                    replacement,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(),
                    new RecordingOutbox(),
                    new MutableTimeProvider(fixture.DueAt.AddSeconds(3))).EnsureAsync(
                        fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(outcome).IsEqualTo(KohRuntimeProvisioningOutcome.Applied);
            }
            await using (var verify = new NoCtfDbContext(options))
            {
                var generations = await verify.RuntimeInstances.AsNoTracking()
                    .OrderBy(runtime => runtime.Generation)
                    .Select(runtime => runtime.Generation)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(generations).IsEquivalentTo([1, 2]);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_shared_runtime_waits_for_cleanup_and_retries_without_early_dispatch(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_runtime_cleanup")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var old = await db.RuntimeInstances.SingleAsync(cancellationToken);
            old.State = RuntimeState.Failed;
            old.FailureCode = RuntimeFailureCode.ProviderUnavailable;
            old.ProviderReceiptJson = "{}";
            old.RunnerAssignmentReleaseToken = Guid.CreateVersion7();
            old.ProcessingVersion = 7;
            await db.SaveChangesAsync(cancellationToken);
            var firstOutbox = new RecordingOutbox();

            var first = await new PostgresKohRuntimeProvisioner(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                firstOutbox,
                new MutableTimeProvider(fixture.DueAt.AddSeconds(1))).EnsureAsync(
                    fixture.CompetitionId,
                    cancellationToken);

            await Assert.That(first).IsEqualTo(KohRuntimeProvisioningOutcome.DeferredCleanup);
            db.ChangeTracker.Clear();
            var firstPass = await db.RuntimeInstances.AsNoTracking()
                .OrderBy(runtime => runtime.Generation)
                .ToListAsync(cancellationToken);
            await Assert.That(firstPass.Count).IsEqualTo(2);
            var stopping = firstPass[0];
            var failedWaiter = firstPass[1];
            await Assert.That(stopping.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(stopping.FailureCode).IsNull();
            await Assert.That(stopping.RunnerAssignmentReleaseToken).IsNull();
            await Assert.That(stopping.ProcessingVersion).IsEqualTo(8);
            await Assert.That(failedWaiter.State).IsEqualTo(RuntimeState.Queued);
            await Assert.That(failedWaiter.ReplacesRuntimeInstanceId).IsEqualTo(stopping.Id);
            await Assert.That(firstOutbox.Published.OfType<StopRuntime>()
                    .Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([stopping.Id]);
            await Assert.That(firstOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await RuntimeWriteBackHandler.Handle(
                new RuntimeStopFailed(
                    stopping.Id,
                    stopping.ProcessingVersion,
                    RuntimeFailureCode.CleanupFailed),
                db,
                cancellationToken);
            db.ChangeTracker.Clear();
            var failed = await db.RuntimeInstances.AsNoTracking()
                .OrderBy(runtime => runtime.Generation)
                .ToListAsync(cancellationToken);
            await Assert.That(failed.All(runtime => runtime.State == RuntimeState.Failed
                && runtime.FailureCode == RuntimeFailureCode.CleanupFailed)).IsTrue();

            var retryOutbox = new RecordingOutbox();
            var retry = await new PostgresKohRuntimeProvisioner(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                retryOutbox,
                new MutableTimeProvider(fixture.DueAt.AddSeconds(2))).EnsureAsync(
                    fixture.CompetitionId,
                    cancellationToken);

            await Assert.That(retry).IsEqualTo(KohRuntimeProvisioningOutcome.DeferredCleanup);
            db.ChangeTracker.Clear();
            var retried = await db.RuntimeInstances.AsNoTracking()
                .OrderBy(runtime => runtime.Generation)
                .ToListAsync(cancellationToken);
            await Assert.That(retried.Count).IsEqualTo(3);
            var retriedOld = retried[0];
            var retryWaiter = retried[2];
            await Assert.That(retriedOld.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(retriedOld.FailureCode).IsNull();
            await Assert.That(retriedOld.RunnerAssignmentReleaseToken).IsNull();
            await Assert.That(retriedOld.ProcessingVersion).IsEqualTo(10);
            await Assert.That(retryWaiter.State).IsEqualTo(RuntimeState.Queued);
            await Assert.That(retryWaiter.ReplacesRuntimeInstanceId).IsEqualTo(retriedOld.Id);
            await Assert.That(retryOutbox.Published.OfType<StopRuntime>()
                    .Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([retriedOld.Id]);
            await Assert.That(retryOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            var acknowledgementOutbox = new RecordingOutbox();
            await RuntimeWriteBackHandler.Handle(
                new RuntimeStopped(retriedOld.Id, retriedOld.ProcessingVersion),
                db,
                acknowledgementOutbox,
                cancellationToken);
            await Assert.That(acknowledgementOutbox.Published.OfType<DispatchRuntime>()
                    .Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([retryWaiter.Id]);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Auto_provisioning_and_admin_start_preserve_the_shared_scope_invariant(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_shared_runtime_lock")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var cleanup = new NoCtfDbContext(options))
            {
                await cleanup.RuntimeInstances.ExecuteDeleteAsync(cancellationToken);
            }

            var autoOutbox = new RecordingOutbox();
            var adminOutbox = new RecordingOutbox();

            var autoProvision = EnsureSharedRuntimeAsync(
                options,
                fixture,
                autoOutbox,
                cancellationToken);
            var adminStart = StartSharedRuntimeAsAdminAsync(
                options,
                fixture,
                adminOutbox,
                cancellationToken);

            var autoOutcome = await autoProvision.WaitAsync(
                TimeSpan.FromSeconds(2), cancellationToken);
            var adminResult = await adminStart.WaitAsync(
                TimeSpan.FromSeconds(2), cancellationToken);
            var validSerialization = adminResult.Runtime is not null
                ? autoOutcome == KohRuntimeProvisioningOutcome.Idempotent
                    && adminResult.Failure is null
                : autoOutcome == KohRuntimeProvisioningOutcome.Applied
                    && adminResult.Failure == RuntimeMutationFailure.InvalidState;
            await Assert.That(validSerialization).IsTrue();
            await Assert.That(autoOutbox.Published.OfType<DispatchRuntime>().Count()
                    + adminOutbox.Published.OfType<DispatchRuntime>().Count())
                .IsEqualTo(1);
            await using var verify = new NoCtfDbContext(options);
            await Assert.That(await verify.RuntimeInstances.AsNoTracking()
                    .CountAsync(runtime =>
                        runtime.CompetitionChallengeId == fixture.CompetitionChallengeId
                        && runtime.TeamId == null,
                        cancellationToken))
                .IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Challenge_access_returns_only_the_callers_flag_and_participant_urls(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_access")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var arrange = new NoCtfDbContext(options))
            {
                var runtime = await arrange.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.State = RuntimeState.Running;
                runtime.Urls =
                [
                    "http://runner.example:32000/play",
                    "http://hill.internal/owner"
                ];
                runtime.ParticipantUrlIndexes = [0];
                runtime.ControlCheckUrl = "http://hill.internal/control";
                await arrange.SaveChangesAsync(cancellationToken);
            }

            await using (var read = new NoCtfDbContext(options))
            {
                var access = await new PostgresKohChallengeAccessReader(read).FindAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.OwnerId,
                    cancellationToken);
                await Assert.That(access).IsNotNull();
                await Assert.That(access!.ControlFlag).IsEqualTo(fixture.Flag);
                await Assert.That(access.Urls)
                    .IsEquivalentTo(["http://runner.example:32000/play"]);
                var outsider = await new PostgresKohChallengeAccessReader(read).FindAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    Guid.CreateVersion7(),
                    cancellationToken);
                await Assert.That(outsider).IsNull();
            }

            await using (var pause = new NoCtfDbContext(options))
            {
                await pause.Competitions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        competition => competition.Status,
                        CompetitionStatus.Paused),
                    cancellationToken);
            }
            await using (var read = new NoCtfDbContext(options))
            {
                var access = await new PostgresKohChallengeAccessReader(read).FindAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.OwnerId,
                    cancellationToken);
                await Assert.That(access).IsNull();
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Observation_writes_one_fact_and_schedules_only_the_next_future_poll(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_poll")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var clock = new MutableTimeProvider(fixture.DueAt.AddSeconds(16));
            var configurations = new KohProducerConfigurationCatalog();
            RecordKohObservation observation;
            var dispatchOutbox = new RecordingOutbox();
            await using (var dispatchDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(
                    new DispatchRuntime(fixture.RuntimeId, 0),
                    dispatchDb,
                    new ChallengeRuntimeTemplateCatalog(),
                    new PostgresRuntimePublishedPortAllocator(
                        dispatchDb,
                        new RuntimePublishedPortRange()),
                    dispatchOutbox,
                    cancellationToken);
            }
            var claim = dispatchOutbox.RunnerPoolMessages
                .OfType<ClaimContainerRuntime>()
                .Single();
            await Assert.That(claim.Definition.UrlBindings).Count().IsEqualTo(1);
            await Assert.That(claim.Definition.ControlCheckUrlBinding).IsNotNull();
            await Assert.That(claim.Definition.Labels["noctf.io/managed"])
                .IsEqualTo("true");
            await Assert.That(claim.Definition.Labels["noctf.io/runtime-instance-id"])
                .IsEqualTo(fixture.RuntimeId.ToString("D"));
            await Assert.That(claim.Definition.Labels["noctf.io/competition-id"])
                .IsEqualTo(fixture.CompetitionId.ToString("D"));
            await Assert.That(claim.Definition.Labels["noctf.io/competition-challenge-id"])
                .IsEqualTo(fixture.CompetitionChallengeId.ToString("D"));
            await Assert.That(claim.Definition.Labels["noctf.io/generation"])
                .IsEqualTo("1");
            await Assert.That(claim.Definition.Labels)
                .DoesNotContainKey("noctf.io/team-id");
            await using (var provisionDb = new NoCtfDbContext(options))
            {
                var runtime = await provisionDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.State = RuntimeState.Provisioning;
                await provisionDb.SaveChangesAsync(cancellationToken);
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisioned(
                        fixture.RuntimeId,
                        0,
                        1,
                        "runner-a",
                        RuntimeProvider.Docker,
                        "{}",
                        ["http://runner.example:32000/play"],
                        [0],
                        null,
                        "http://hill.internal/control"),
                    provisionDb,
                    new RecordingOutbox(),
                    cancellationToken);
            }

            await using (var readDb = new NoCtfDbContext(options))
            {
                var polling = new KohPollingHandler(
                    readDb,
                    new FixedKohControlClient(fixture.Flag),
                    configurations,
                    clock);
                observation = (await polling.Handle(
                    new PollKohChallenge(
                        fixture.CompetitionId,
                        fixture.CompetitionChallengeId,
                        0,
                        0,
                        fixture.DueAt,
                        fixture.DueAt),
                    cancellationToken))!;
            }

            await Assert.That(observation.Result).IsEqualTo(ScoringResult.Correct);
            await Assert.That(observation.TeamId).IsEqualTo(fixture.TeamId);
            var outbox = new RecordingOutbox();
            await using (var writeDb = new NoCtfDbContext(options))
            {
                var writer = new KohObservationHandler(
                    writeDb,
                    outbox,
                    configurations,
                    clock);
                await writer.Handle(observation, cancellationToken);
            }

            await using (var verify = new NoCtfDbContext(options))
            {
                var fact = await verify.ScoringEvents.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(fact.Kind).IsEqualTo(ScoringEventKind.KohObservation);
                await Assert.That(fact.Result).IsEqualTo(ScoringResult.Correct);
                await Assert.That(fact.TeamId).IsEqualTo(fixture.TeamId);
                await Assert.That(
                    await verify.Competitions.Select(item => item.LeaderboardRevision)
                        .SingleAsync(cancellationToken))
                    .IsEqualTo(1);
            }

            var next = outbox.Scheduled.Single();
            await Assert.That(next.At).IsEqualTo(fixture.DueAt.AddSeconds(20));
            await Assert.That(next.Message).IsTypeOf<PollKohChallenge>();
            var projection = outbox.Published.Single();
            await Assert.That(projection).IsTypeOf<InvalidateLeaderboard>();
            await Assert.That(((InvalidateLeaderboard)projection).CompetitionId)
                .IsEqualTo(fixture.CompetitionId);
            await Assert.That(outbox.FlushCount).IsEqualTo(1);

            await using (var changeDb = new NoCtfDbContext(options))
            {
                await changeDb.Competitions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.ConfigurationRevision, 1),
                    cancellationToken);
            }
            var staleOutbox = new RecordingOutbox();
            await using (var staleDb = new NoCtfDbContext(options))
            {
                var writer = new KohObservationHandler(
                    staleDb,
                    staleOutbox,
                    configurations,
                    clock);
                await writer.Handle(observation, cancellationToken);
                await Assert.That(await staleDb.ScoringEvents.CountAsync(cancellationToken))
                    .IsEqualTo(1);
            }
            var replacement = (PollKohChallenge)staleOutbox.Scheduled.Single().Message;
            await Assert.That(replacement.CompetitionConfigurationRevision).IsEqualTo(1);

            await using (var resumeDb = new NoCtfDbContext(options))
            {
                await resumeDb.Competitions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.RunningSince,
                        fixture.DueAt.AddMinutes(1)),
                    cancellationToken);
            }
            await using (var staleEpochDb = new NoCtfDbContext(options))
            {
                var staleEpoch = await new KohPollingHandler(
                    staleEpochDb,
                    new FixedKohControlClient(fixture.Flag),
                    configurations,
                    clock).Handle(
                        new PollKohChallenge(
                            fixture.CompetitionId,
                            fixture.CompetitionChallengeId,
                            1,
                            0,
                            fixture.DueAt,
                            fixture.DueAt.AddSeconds(20)),
                        cancellationToken);
                await Assert.That(staleEpoch).IsNull();
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Observation_and_pause_atomically_advance_the_shared_leaderboard_revision(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_revision")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var barrier = new CompetitionLeaderboardUpdateBarrier();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(barrier)
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var observationOutbox = new RecordingOutbox();
            var lifecycleOutbox = new RecordingOutbox();
            var clock = new MutableTimeProvider(fixture.DueAt.AddSeconds(1));
            var observation = new RecordKohObservation(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.TeamId,
                ScoringResult.Correct,
                null,
                0,
                0,
                fixture.DueAt,
                fixture.DueAt,
                clock.GetUtcNow());

            async Task RecordObservationAsync()
            {
                await using var observationDb = new NoCtfDbContext(options);
                await new KohObservationHandler(
                    observationDb,
                    observationOutbox,
                    new KohProducerConfigurationCatalog(),
                    clock).Handle(observation, cancellationToken);
            }

            async Task<bool> PauseCompetitionAsync()
            {
                await using var lifecycleDb = new NoCtfDbContext(options);
                await using var transaction = await lifecycleDb.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.ReadCommitted,
                    cancellationToken);
                var applied = await new CompetitionLifecycleStore(
                    lifecycleDb,
                    null!,
                    lifecycleOutbox).TryTransitionWithAuditAsync(
                        fixture.CompetitionId,
                        CompetitionStatus.Running,
                        CompetitionStatus.Paused,
                        fixture.OwnerId,
                        "pause",
                        false,
                        CompetitionLifecycleEffects.None,
                        cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return applied;
            }

            barrier.Enable();
            var observationTask = RecordObservationAsync();
            var lifecycleTask = PauseCompetitionAsync();
            await Task.WhenAll(observationTask, lifecycleTask);

            await Assert.That(await lifecycleTask).IsTrue();
            await Assert.That(barrier.Arrivals).IsEqualTo(2);
            await using (var verify = new NoCtfDbContext(options))
            {
                var competition = await verify.Competitions.AsNoTracking()
                    .SingleAsync(
                        item => item.Id == fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Paused);
                await Assert.That(competition.LeaderboardRevision).IsEqualTo(2);
                var fact = await verify.ScoringEvents.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(fact.Kind).IsEqualTo(ScoringEventKind.KohObservation);
                var transition = await verify.Set<CompetitionLifecycleAudit>()
                    .AsNoTracking()
                    .SingleAsync(
                        audit => audit.CompetitionId == fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(transition.From).IsEqualTo(CompetitionStatus.Running);
                await Assert.That(transition.To).IsEqualTo(CompetitionStatus.Paused);
            }

            var projections = observationOutbox.Published
                .Concat(lifecycleOutbox.Published)
                .OfType<InvalidateLeaderboard>()
                .ToArray();
            await Assert.That(projections).Count().IsEqualTo(2);
            await Assert.That(projections.All(
                projection => projection.CompetitionId == fixture.CompetitionId)).IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var dueAt = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        const string flag = "NOCTF{raw-koh-control}";
        var runtime = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition(
                "registry.example/hill:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 },
                Security: new(true, true, true, ["ALL"], [])),
            Limits: new(268_435_456, 500_000_000, 128),
            UrlBindings:
            [
                new("http://{HOST}:{PORT}/play", RuntimeExposure.Participants, ContainerPort: 8080)
            ],
            ControlCheckUrlBinding: new(
                "http://{HOST}:{PORT}/control",
                RuntimeExposure.OwnerOnly,
                ContainerPort: 8080));
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "koh-owner",
            NormalizedUserName = "KOH-OWNER",
            Email = "koh-owner@example.test",
            NormalizedEmail = "KOH-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = dueAt,
            UpdatedAt = dueAt
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "KoH",
            OwnerId = ownerId,
            Mode = GameMode.Koh,
            Status = CompetitionStatus.Running,
            ConfigurationJson = JsonSerializer.Serialize(
                new KohConfiguration(1, 5, 10),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RunningSince = dueAt,
            StartAt = dueAt.AddHours(-1),
            EndAt = dueAt.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = dueAt,
            UpdatedAt = dueAt,
            ConfigurationUpdatedAt = dueAt
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Koh,
            Title = "Hill",
            DefinitionJson = JsonSerializer.Serialize(
                new KohChallengeConfiguration(1, runtime),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = dueAt,
            UpdatedAt = dueAt
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(
                new KohChallengeConfiguration(1),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = dueAt
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Blue",
            NormalizedName = "BLUE",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = "0123456789abcdef0123456789abcdef",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = dueAt
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(),
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            SpecificationKind = SpecificationKind.RuntimeDefinition,
            SpecificationId = competitionChallengeId,
            Flag = flag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            CreatedAt = dueAt
        });
        var runtimeId = Guid.CreateVersion7();
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = null,
            Purpose = RuntimePurpose.Player,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "default",
            RunnerId = "runner-a",
            State = RuntimeState.Queued,
            CreatedAt = dueAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(dueAt, ownerId, competitionId, competitionChallengeId, teamId, runtimeId, flag);
    }

    private sealed record Fixture(
        DateTimeOffset DueAt,
        Guid OwnerId,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid RuntimeId,
        string Flag);

    private static async Task<KohRuntimeProvisioningOutcome> EnsureSharedRuntimeAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        RecordingOutbox outbox,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PostgresKohRuntimeProvisioner(
            db,
            new ChallengeRuntimeTemplateCatalog(),
            new FixedRuntimePlacementPolicy(),
            outbox,
            new MutableTimeProvider(fixture.DueAt.AddSeconds(1))).EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);
    }

    private static async Task<RuntimeMutationResult> StartSharedRuntimeAsAdminAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        RecordingOutbox outbox,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new AdminRuntimeStore(
            db,
            new ChallengeRuntimeTemplateCatalog(),
            new FixedRuntimePlacementPolicy(),
            null!,
            outbox).MutateAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                null,
                RuntimeAction.Start,
                null,
                fixture.DueAt.AddSeconds(1),
                cancellationToken);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedKohControlClient(string flag) : IKohControlClient
    {
        public Task<KohControlResponse> ObserveAsync(
            Uri controlUrl,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(KohControlResponse.Success(Encoding.UTF8.GetBytes(flag)));
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerPoolMessages { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];
        public int FlushCount { get; private set; }

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

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage
        {
            RunnerPoolMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync()
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
