using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Runner.Messages;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTestRuntimePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Template_test_runtime_renews_only_during_final_ten_minutes(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_test_runtime_renewal_window")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            await using var db = new NoCtfDbContext(options);
            var runtime = RuntimeInstanceGeneratedCatalog.Create(RuntimePurpose.TemplateTest);
            runtime.Id = Guid.CreateVersion7(fixture.Now.AddSeconds(1));
            runtime.ChallengeId = fixture.ChallengeId;
            runtime.RuntimeKind = RuntimeKind.Container;
            runtime.RuntimeProvider = RuntimeProvider.Docker;
            runtime.State = RuntimeState.Running;
            runtime.CreatedAt = fixture.Now;
            runtime.RunningAt = fixture.Now;
            runtime.ExpiresAt = fixture.Now.AddMinutes(52);
            db.RuntimeInstances.Add(runtime);
            await db.SaveChangesAsync(cancellationToken);
            var store = new ChallengeTestRuntimeStore(db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                new RecordingOutbox());

            var early = await store.MutateAsync(new(
                fixture.ChallengeId, fixture.OwnerId, false,
                RuntimeAction.Extend, TimeSpan.FromMinutes(30), fixture.Now),
                cancellationToken);
            await Assert.That(early.Failure)
                .IsEqualTo(RuntimeMutationFailure.ExtensionTooEarly);
            await db.Entry(runtime).ReloadAsync(cancellationToken);
            await Assert.That(runtime.ExpiresAt).IsEqualTo(fixture.Now.AddMinutes(52));

            runtime.ExpiresAt = fixture.Now.AddMinutes(9);
            await db.SaveChangesAsync(cancellationToken);
            var renewed = await store.MutateAsync(new(
                fixture.ChallengeId, fixture.OwnerId, false,
                RuntimeAction.Extend, TimeSpan.FromMinutes(30), fixture.Now),
                cancellationToken);
            await Assert.That(renewed.Failure).IsNull();
            await Assert.That(renewed.Runtime?.ExpiresAt)
                .IsEqualTo(fixture.Now.AddMinutes(39));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task StartDispatchResetAndStop_KeepTemplateScopeAndDynamicFlagAtomic(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_test_runtime")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var templates = new ChallengeRuntimeTemplateCatalog();
            var outbox = new RecordingOutbox();

            await using var db = new NoCtfDbContext(options);
            var store = new ChallengeTestRuntimeStore(
                db,
                templates,
                new FixedRuntimePlacementPolicy(),
                outbox);
            var started = await store.MutateAsync(new(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                RuntimeAction.Start,
                null,
                fixture.Now), cancellationToken);

            await Assert.That(started.Failure).IsNull();
            await Assert.That(started.Runtime).IsNotNull();
            var runtimeId = started.Runtime!.Id;
            var entity = await db.RuntimeInstances.SingleAsync(
                runtime => runtime.Id == runtimeId,
                cancellationToken);
            await Assert.That(entity.CompetitionId).IsNull();
            await Assert.That(entity.CompetitionChallengeId).IsNull();
            await Assert.That(entity.ChallengeId).IsEqualTo(fixture.ChallengeId);
            await Assert.That(entity.Purpose).IsEqualTo(RuntimePurpose.TemplateTest);
            var inventory = new AdminRuntimeStore(db, templates, new FixedRuntimePlacementPolicy(),
                new PerTeamRuntimeFlagStore(db), outbox);
            var globalTests = await inventory.ListActiveContainersAsync(
                new(Scope: PlatformRuntimeScope.ChallengeTest), null, null, 50, cancellationToken);
            await Assert.That(globalTests).HasSingleItem();
            await Assert.That(globalTests[0].Runtime.Id).IsEqualTo(runtimeId);
            var competitionOnly = await inventory.ListActiveContainersAsync(
                new(Scope: PlatformRuntimeScope.Competition), null, null, 50, cancellationToken);
            await Assert.That(competitionOnly).IsEmpty();
            await Assert.That(entity.TestFlagDelivery).IsEqualTo(RuntimeTestFlagDelivery.Environment);
            await Assert.That(entity.TestFlagState).IsEqualTo(RuntimeTestFlagState.Pending);

            var updatedDefinition = (CtfChallengeDefinition)(await db.Challenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == fixture.ChallengeId, cancellationToken))
                .Definition!;
            ((ContainerChallengeRuntimeTemplate)updatedDefinition.Runtime!).Services[0].Image =
                "challenge:test-v2";
            var definitionBlocked = await new ChallengeBankStore(db).UpdateAsync(new(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Template test runtime",
                null,
                "PWN",
                updatedDefinition,
                fixture.Now.AddMilliseconds(1)), cancellationToken);
            await Assert.That(definitionBlocked.State)
                .IsEqualTo(ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict);

            var flag = await db.ChallengeFlags.SingleAsync(candidate =>
                candidate.ChallengeId == fixture.ChallengeId
                && candidate.SpecificationKind == SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == runtimeId,
                cancellationToken);
            await Assert.That(flag.Flag).StartsWith("test{");
            var listedTemplateFlags = await new ChallengeFlagManagementStore(db, templates)
                .ListAsync(
                    ChallengeFlagScope.Template(fixture.ChallengeId),
                    fixture.OwnerId,
                    false,
                    false,
                    cancellationToken);
            await Assert.That(listedTemplateFlags).IsNotNull();
            await Assert.That(listedTemplateFlags!).IsEmpty();

            var duplicate = await store.MutateAsync(new(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                RuntimeAction.Start,
                null,
                fixture.Now.AddMilliseconds(1)), cancellationToken);
            await Assert.That(duplicate.Runtime?.Id).IsEqualTo(runtimeId);
            await Assert.That(await db.RuntimeInstances.CountAsync(cancellationToken)).IsEqualTo(1);
            var deleteFailure = await new ChallengeBankStore(db).SoftDeleteAsync(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                fixture.Now.AddSeconds(1),
                cancellationToken);
            await Assert.That(deleteFailure).IsEqualTo(ChallengeTemplateDeleteFailure.InUse);

            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await BackendMessageOperations.DispatchRuntimeAsync(
                dispatch,
                db,
                templates,
                new FixedRuntimePlacementPolicy(),
                new FixedCapacityGate("runner-test"),
                outbox,
                TimeProvider.System,
                cancellationToken);
            var provision = outbox.RunnerNodeMessages.OfType<ProvisionContainerRuntime>().Single();
            await Assert.That(provision.Definition.Services[0].Environment!["CHALLENGE_FLAG"])
                .IsEqualTo(flag.Flag);
            await Assert.That(provision.Definition.Labels["noctf.io/job-kind"])
                .IsEqualTo("challenge-test-runtime");

            var runningAt = fixture.Now.AddSeconds(2);
            await RuntimeWriteBackHandler.Handle(
                new RuntimeProvisioned(
                    runtimeId,
                    "runner-test",
                    RuntimeProvider.Docker,
                    RuntimeReceiptTestData.ContainerData(runtimeId),
                    [new RuntimeAccessEndpointMapping(0, "tcp://127.0.0.1:31337", null, null)],
                    runningAt.AddHours(1)),
                db,
                outbox,
                cancellationToken);
            await db.Entry(entity).ReloadAsync(cancellationToken);
            await db.Entry(flag).ReloadAsync(cancellationToken);
            await Assert.That(entity.State).IsEqualTo(RuntimeState.Running);
            await Assert.That(entity.TestFlagState).IsEqualTo(RuntimeTestFlagState.Succeeded);
            await Assert.That(flag.ValidStart).IsNotNull();

            var current = await store.FindAsync(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                cancellationToken);
            await Assert.That(current?.TestFlag).IsEqualTo(flag.Flag);
            await Assert.That(current?.AccessEndpoints?.Select(endpoint => endpoint.DirectAddress).OfType<string>())
                .IsEquivalentTo(["tcp://127.0.0.1:31337"]);
            await Assert.That(await store.FindAsync(
                    fixture.ChallengeId,
                    Guid.CreateVersion7(),
                    false,
                    cancellationToken))
                .IsNull();

            var reset = await store.MutateAsync(new(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                RuntimeAction.Reset,
                null,
                runningAt.AddSeconds(1)), cancellationToken);
            await Assert.That(reset.Runtime).IsNotNull();
            await Assert.That(reset.Runtime!.Id).IsNotEqualTo(runtimeId);
            await db.Entry(flag).ReloadAsync(cancellationToken);
            await Assert.That(flag.ValidUntil).IsNotNull();
            await Assert.That(await db.RuntimeInstances.CountAsync(cancellationToken)).IsEqualTo(2);

            var stopped = await store.MutateAsync(new(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                RuntimeAction.Stop,
                null,
                runningAt.AddSeconds(2)), cancellationToken);
            await Assert.That(stopped.Runtime?.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopped.Runtime?.FlagState).IsEqualTo(RuntimeTestFlagState.Canceled);

            await db.Entry(entity).ReloadAsync(cancellationToken);
            entity.State = RuntimeState.Stopped;
            entity.StoppedAt = runningAt.AddSeconds(2);
            await db.SaveChangesAsync(cancellationToken);

            var queued = await store.MutateAsync(new(
                fixture.ChallengeId,
                fixture.OwnerId,
                false,
                RuntimeAction.Start,
                null,
                runningAt.AddSeconds(3)), cancellationToken);
            await Assert.That(queued.Runtime?.State).IsEqualTo(RuntimeState.Queued);
            var queuedRuntimeId = queued.Runtime!.Id;

            await using var stopDb = new NoCtfDbContext(options);
            await using var stopTransaction = await stopDb.Database.BeginTransactionAsync(cancellationToken);
            _ = await ChallengeTemplateCriticalSection.AcquireAsync(
                stopDb,
                fixture.ChallengeId,
                cancellationToken);
            var stoppingBeforeDispatch = await stopDb.RuntimeInstances.SingleAsync(
                runtime => runtime.Id == queuedRuntimeId,
                cancellationToken);
            stoppingBeforeDispatch.State = RuntimeState.Stopped;
            stoppingBeforeDispatch.StoppedAt = runningAt.AddSeconds(4);
            await stopDb.SaveChangesAsync(cancellationToken);

            var raceCapacity = new FixedCapacityGate("runner-race");
            var dispatchStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatchTask = Task.Run(async () =>
            {
                await using var dispatchDb = new NoCtfDbContext(options);
                await using var dispatchTransaction =
                    await dispatchDb.Database.BeginTransactionAsync(cancellationToken);
                dispatchStarted.SetResult();
                await BackendMessageOperations.DispatchRuntimeAsync(
                    new DispatchRuntime(queuedRuntimeId),
                    dispatchDb,
                    templates,
                    new FixedRuntimePlacementPolicy(),
                    raceCapacity,
                    new RecordingOutbox(),
                    TimeProvider.System,
                    cancellationToken);
                await dispatchTransaction.CommitAsync(cancellationToken);
            }, cancellationToken);
            await dispatchStarted.Task.WaitAsync(cancellationToken);
            await Task.Delay(100, cancellationToken);
            await stopTransaction.CommitAsync(cancellationToken);
            await dispatchTask;

            db.ChangeTracker.Clear();
            var afterRace = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                runtime => runtime.Id == queuedRuntimeId,
                cancellationToken);
            await Assert.That(afterRace.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(afterRace.CapacityAllocations.Items).IsEmpty();
            // Optimistic concurrency may inspect a queued snapshot and attempt a claim
            // before the concurrent stop commits; only the persisted outcome is fenced.
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var ownerId = Guid.CreateVersion7(now);
        var challengeId = Guid.CreateVersion7(now.AddMilliseconds(1));
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "challenge-owner",
            NormalizedUserName = "CHALLENGE-OWNER",
            Email = "challenge-owner@example.test",
            PasswordHash = "test",
            Role = UserRole.Organizer,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Visibility = ChallengeVisibility.Private,
            Title = "Template test runtime",
            Direction = "PWN",
            Definition = TestConfigurations.Definition(GameMode.Ctf, JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    null,
                    null,
                    Runtime: new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition([new RuntimeServiceDefinition("main", "challenge:test", FlagEnvironmentVariableName: "CHALLENGE_FLAG")]),
                        new RuntimeResourceLimits(67_108_864, 100, 64),
                        TtlSeconds: 3600,
                        UrlBindings:
                        [
                            new RuntimeUrlBinding(
                                "tcp://{HOST}:{PORT}",
                                RuntimeExposure.OwnerOnly,
                                ContainerPort: 31337)
                        ],
                        FlagSource: RuntimeFlagSource.PerTeam),
                    FlagTemplate: new PerTeamFlagTemplate("test", "[GUID:N]", false)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, ownerId, challengeId);
    }

    private sealed record Fixture(DateTimeOffset Now, Guid OwnerId, Guid ChallengeId);

    private sealed class RecordingOutbox : IPostCommitMessagePublisher
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerNodeMessages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage
        {
            RunnerNodeMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class FixedCapacityGate(string runnerId) : IRunnerCapacityGate
    {
        public int ClaimCount { get; private set; }

        public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
            string runnerPool,
            string candidateRunnerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(RunnerHeartbeatStatus.Online);

        public Task<RunnerPoolInventory> GetPoolInventoryAsync(
            string runnerPool,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RunnerPoolInventory(
                RunnerPoolInventoryAvailability.Available,
                [runnerId]));

        public Task<RunnerCapacityClaim> TryClaimAsync(
            RunnerCapacityRequest request,
            CancellationToken cancellationToken)
        {
            ClaimCount++;
            return Task.FromResult(new RunnerCapacityClaim(
                RunnerCapacityAvailability.Claimed,
                runnerId,
                RunnerCapacityClaimState.Acquired));
        }

        public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
            RunnerCapacityRequest request,
            string candidateRunnerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RunnerCapacityClaim(
                RunnerCapacityAvailability.Claimed,
                candidateRunnerId,
                RunnerCapacityClaimState.AlreadyOwned));

        public Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
            Guid runtimeInstanceId,
            string candidateRunnerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(RunnerCapacityReleaseOutcome.Released);
    }
}
