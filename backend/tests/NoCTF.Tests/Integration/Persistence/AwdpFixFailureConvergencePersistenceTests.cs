using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Runner.Messages;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[NotInParallel]
public sealed class AwdpFixFailureConvergencePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Successful_Fix_events_expose_the_correct_result_for_new_and_legacy_payloads(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedBaseAsync(options, cancellationToken);
            var operation = await AddFixAsync(
                options,
                fixture,
                RuntimeState.Running,
                cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            var eventStore = new CompetitionEventStore(db, outbox);
            var resultStore = new InternalResultStore(db, outbox, eventStore);
            var resolvedAt = fixture.Now.AddSeconds(1);
            var disposition = await resultStore.RecordAwdpAsync(
                AwdpFixResult.Create(
                    operation.GameplayFactId,
                    operation.RuntimeInstanceId,
                    AwdpFixOutcome.DefenseSucceeded,
                    resolvedAt),
                cancellationToken);

            await Assert.That(disposition).IsEqualTo(InternalResultDisposition.Applied);
            db.ChangeTracker.Clear();
            var current = await db.CompetitionEvents.AsNoTracking().SingleAsync(
                item => item.Kind == CompetitionEventKind.AwdpFixResolved,
                cancellationToken);
            await Assert.That(current.GameplayFactState)
                .IsEqualTo(GameplayFactState.Completed);
            await Assert.That(current.GameplayFactResult)
                .IsEqualTo(GameplayFactResult.Correct);

            var legacyId = Guid.CreateVersion7(fixture.Now);
            db.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = legacyId,
                CompetitionId = fixture.CompetitionId,
                Kind = CompetitionEventKind.AwdpFixResolved,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Public,
                SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.GameplayFact,
                SubjectId = Guid.CreateVersion7(),
                RelatedType = NoCTF.Domain.Shared.EntityReferenceKind.Team,
                RelatedId = fixture.TeamId,
                PayloadJson = AwdpFixResolvedEventPayload.Create(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    operation.RuntimeInstanceId,
                    fixture.TeamId,
                    fixture.CompetitionChallengeId,
                    AwdpFixOutcome.DefenseSucceeded,
                    null,
                    fixture.Now).Serialize(),
                OccurredAt = fixture.Now
            });
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();

            var history = await eventStore.QueryAsync(new(
                fixture.CompetitionId,
                fixture.UserId,
                Kind: CompetitionEventKind.AwdpFixResolved,
                MinimumLevel: null,
                TeamId: null,
                ActorUserId: null,
                CompetitionChallengeId: null,
                RuntimeInstanceId: null,
                From: null,
                To: null,
                BeforeOccurredAt: null,
                BeforeId: null,
                Limit: 10), cancellationToken);
            var legacy = history.Items!.Single(item => item.Id == legacyId);
            await Assert.That(legacy.GameplayFactKind)
                .IsEqualTo(GameplayFactKind.FixAttempt);
            await Assert.That(legacy.GameplayFactState)
                .IsEqualTo(GameplayFactState.Completed);
            await Assert.That(legacy.GameplayFactResult)
                .IsEqualTo(GameplayFactResult.Correct);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Expiry_converges_every_runtime_terminal_shape_idempotently(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedBaseAsync(options, cancellationToken);
            foreach (var state in new[]
                     {
                         RuntimeState.Running,
                         RuntimeState.Stopping,
                         RuntimeState.Stopped,
                         RuntimeState.Failed
                     })
            {
                var operation = await AddFixAsync(
                    options,
                    fixture,
                    state,
                    cancellationToken);
                await using var db = new NoCtfDbContext(options);
                var outbox = new RecordingOutbox();
                var events = new CompetitionEventStore(db, outbox);
                var deadline = fixture.Now.AddMinutes(1);
                var clock = new FakeTimeProvider(deadline.AddSeconds(1));
                var message = new ExpireAwdpFixVerification(
                    operation.GameplayFactId,
                    operation.RuntimeInstanceId,
                    deadline,
                    "runner-a");

                await BackendMessageOperations.ExpireAwdpFixVerificationAsync(
                    message,
                    db,
                    outbox,
                    clock,
                    cancellationToken,
                    events);
                await BackendMessageOperations.ExpireAwdpFixVerificationAsync(
                    message,
                    db,
                    outbox,
                    clock,
                    cancellationToken,
                    events);

                db.ChangeTracker.Clear();
                var fact = await db.GameplayFacts.AsNoTracking().SingleAsync(
                    item => item.Id == operation.GameplayFactId,
                    cancellationToken);
                var runtime = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                    item => item.Id == operation.RuntimeInstanceId,
                    cancellationToken);
                await Assert.That(fact.State).IsEqualTo(GameplayFactState.PlatformFailed);
                await Assert.That(fact.Result).IsNull();
                await Assert.That(fact.FailureCode)
                    .IsEqualTo(GameplayFactFailureCode.AwdpPlatformFailed);
                await Assert.That(runtime.State).IsEqualTo(
                    state == RuntimeState.Running ? RuntimeState.Stopping : state);
                await Assert.That(outbox.Messages.OfType<GameplayFactStateChanged>().Count())
                    .IsEqualTo(1);
                await Assert.That(await db.CompetitionEvents.AsNoTracking().CountAsync(
                    item => item.GameplayFactId == operation.GameplayFactId
                        && item.Kind == CompetitionEventKind.AwdpFixResolved,
                    cancellationToken)).IsEqualTo(1);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Admin_termination_and_competition_cleanup_end_linked_Fix_facts(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedBaseAsync(options, cancellationToken);
            var adminTarget = await AddFixAsync(
                options,
                fixture,
                RuntimeState.Running,
                cancellationToken);
            var cleanupTarget = await AddFixAsync(
                options,
                fixture,
                RuntimeState.Running,
                cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            var events = new CompetitionEventStore(db, outbox);
            var admin = new AdminRuntimeStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                new PostgresPerTeamRuntimeFlagStore(db),
                outbox,
                events);
            var terminated = await admin.TerminateAsync(
                fixture.CompetitionId,
                adminTarget.RuntimeInstanceId,
                fixture.UserId,
                fixture.Now.AddSeconds(5),
                cancellationToken);
            await Assert.That(terminated.Failure).IsNull();

            await BackendMessageOperations.CleanupCompetitionRuntimesAsync(
                new CleanupCompetitionRuntimes(fixture.CompetitionId),
                db,
                outbox,
                new FakeTimeProvider(fixture.Now.AddSeconds(10)),
                cancellationToken,
                events);

            db.ChangeTracker.Clear();
            var facts = await db.GameplayFacts.AsNoTracking()
                .Where(item => item.Id == adminTarget.GameplayFactId
                    || item.Id == cleanupTarget.GameplayFactId)
                .ToListAsync(cancellationToken);
            await Assert.That(facts).Count().IsEqualTo(2);
            await Assert.That(facts.All(item =>
                    item.State == GameplayFactState.PlatformFailed
                    && item.FailureCode == GameplayFactFailureCode.AwdpPlatformFailed))
                .IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runtime_stop_writeback_ends_the_linked_Fix_fact(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedBaseAsync(options, cancellationToken);
            var operation = await AddFixAsync(
                options,
                fixture,
                RuntimeState.Stopping,
                cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            await RuntimeWriteBackHandler.Handle(
                new RuntimeStopped(operation.RuntimeInstanceId, "runner-a"),
                db,
                outbox,
                cancellationToken,
                new CompetitionEventStore(db, outbox));

            db.ChangeTracker.Clear();
            var fact = await db.GameplayFacts.AsNoTracking().SingleAsync(
                item => item.Id == operation.GameplayFactId,
                cancellationToken);
            var runtime = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                item => item.Id == operation.RuntimeInstanceId,
                cancellationToken);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.PlatformFailed);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopped);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Runner_PlatformFailed_result_converges_fact_and_runtime(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedBaseAsync(options, cancellationToken);
            var operation = await AddFixAsync(
                options,
                fixture,
                RuntimeState.Running,
                cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            var store = new InternalResultStore(
                db,
                outbox,
                new CompetitionEventStore(db, outbox));
            var disposition = await store.RecordAwdpAsync(
                AwdpFixResult.Create(
                    operation.GameplayFactId,
                    operation.RuntimeInstanceId,
                    AwdpFixOutcome.PlatformFailed,
                    fixture.Now.AddSeconds(1)),
                cancellationToken);

            await Assert.That(disposition).IsEqualTo(InternalResultDisposition.Applied);
            db.ChangeTracker.Clear();
            var fact = await db.GameplayFacts.AsNoTracking().SingleAsync(
                item => item.Id == operation.GameplayFactId,
                cancellationToken);
            var runtime = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                item => item.Id == operation.RuntimeInstanceId,
                cancellationToken);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.PlatformFailed);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(outbox.Messages.OfType<StopRuntime>()).Count().IsEqualTo(1);
        });
    }

    private static async Task<OperationFixture> AddFixAsync(
        DbContextOptions<NoCtfDbContext> options,
        BaseFixture fixture,
        RuntimeState runtimeState,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var runtimeId = Guid.CreateVersion7();
        var factId = Guid.CreateVersion7();
        var patchId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        db.Files.Add(new StoredFile
        {
            Id = fileId,
            ObjectKey = $"fix/{fileId:N}",
            FileName = "fix.tar.gz",
            ContentType = "application/gzip",
            ByteLength = 1,
            Sha256 = new byte[32],
            CreatedAt = fixture.Now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.TeamId,
            Purpose = RuntimePurpose.AwdpTarget,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerId = "runner-a",
            State = runtimeState,
            FailureCode = runtimeState == RuntimeState.Failed
                ? RuntimeFailureCode.ProviderUnavailable
                : null,
            ProviderReceiptJson = "{}",
            GameplayFactId = factId,
            CreatedAt = fixture.Now,
            RunningAt = fixture.Now,
            StoppedAt = runtimeState == RuntimeState.Stopped ? fixture.Now : null,
            ExpiresAt = fixture.Now.AddHours(1)
        });
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.TeamId,
            UploadedByUserId = fixture.UserId,
            RuntimeInstanceId = runtimeId,
            FileId = fileId,
            UploadedAt = fixture.Now
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = factId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.TeamId,
            ActorUserId = fixture.UserId,
            Kind = GameplayFactKind.FixAttempt,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = patchId,
            OccurredAt = fixture.Now,
            State = GameplayFactState.Processing,
            UpdatedAt = fixture.Now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(runtimeId, factId);
    }

    private static async Task<BaseFixture> SeedBaseAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "awdp-convergence",
            NormalizedUserName = "AWDP-CONVERGENCE",
            Email = "awdp-convergence@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = userId,
            Title = "AWDP convergence",
            Mode = GameMode.Awdp,
            Status = CompetitionStatus.Running,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = userId,
            Mode = GameMode.Awdp,
            Title = "AWDP convergence challenge",
            Visibility = ChallengeVisibility.Private,
            DefinitionJson = new GameModeChallengeConfigurationCatalog()
                .GetDefaultJson(GameMode.Awdp),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Awdp),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "AWDP convergence team",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('c', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, competitionChallengeId, userId, teamId);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_awdp_convergence_{Guid.NewGuid():N}")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message) => Add(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => Add(message);
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            Add(message);
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => Add(message);
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;

        private ValueTask Add<T>(T message)
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record BaseFixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid UserId,
        Guid TeamId);

    private sealed record OperationFixture(Guid RuntimeInstanceId, Guid GameplayFactId);
}
