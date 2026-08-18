using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("AwdpFixFence")]
public sealed class AwdpFixExecutionFencePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Redelivery_cleans_the_consumed_target_without_rerunning_the_checker(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);
            var request = Request(fixture);

            var staleGeneration = await AcquireAsync(
                options,
                request with { Generation = 2 },
                cancellationToken);
            await Assert.That(staleGeneration.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Superseded);

            var first = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(first.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Execute);
            await Assert.That(first.RuntimeProcessingVersion).IsEqualTo(1);

            var uncertain = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(uncertain.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Recover);
            await Assert.That(uncertain.RuntimeProcessingVersion).IsEqualTo(2);

            var resumedCleanup = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(resumedCleanup.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Recover);
            await Assert.That(resumedCleanup.RuntimeProcessingVersion).IsEqualTo(2);

            await using (var staleResultDb = new NoCtfDbContext(options))
            {
                var staleResult = await new InternalResultStore(
                    staleResultDb,
                    new RecordingOutbox()).RecordAwdpAsync(AwdpFixResult.Create(
                        fixture.GameplayFactId,
                        fixture.RuntimeInstanceId,
                        1,
                        first.RuntimeProcessingVersion,
                        AwdpFixOutcome.DefenseSucceeded,
                        fixture.Now.AddSeconds(1)), cancellationToken);
                await Assert.That(staleResult).IsEqualTo(InternalResultDisposition.Superseded);
            }

            var replayOutbox = new RecordingOutbox();
            await using (var replayDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(new ReplayAwdpFixVerification(
                    fixture.GameplayFactId,
                    fixture.RuntimeInstanceId,
                    1,
                    uncertain.RuntimeProcessingVersion,
                    fixture.RunnerPool,
                    fixture.RunnerId,
                    fixture.Now.AddSeconds(2)),
                    replayDb,
                    replayOutbox,
                    cancellationToken);
            }

            await using (var verification = new NoCtfDbContext(options))
            {
                var runtimes = await verification.RuntimeInstances.AsNoTracking()
                    .Where(runtime => runtime.GameplayFactId == fixture.GameplayFactId)
                    .OrderBy(runtime => runtime.Generation)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(runtimes.Length).IsEqualTo(1);
                await Assert.That(runtimes[0].State).IsEqualTo(RuntimeState.Stopped);
                var fact = await verification.GameplayFacts.AsNoTracking()
                    .SingleAsync(item => item.Id == fixture.GameplayFactId, cancellationToken);
                await Assert.That(fact.State).IsEqualTo(GameplayFactState.PlatformFailed);
                await Assert.That(fact.FailureCode)
                    .IsEqualTo(GameplayFactFailureCode.CheckerPlatformError);
            }
            await Assert.That(replayOutbox.Messages.OfType<DispatchRuntime>().Count())
                .IsEqualTo(0);

            var obsoleteMessage = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(obsoleteMessage.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Superseded);

            await using (var duplicateDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(new ReplayAwdpFixVerification(
                    fixture.GameplayFactId,
                    fixture.RuntimeInstanceId,
                    1,
                    uncertain.RuntimeProcessingVersion,
                    fixture.RunnerPool,
                    fixture.RunnerId,
                    fixture.Now.AddSeconds(2)),
                    duplicateDb,
                    replayOutbox,
                    cancellationToken);
            }
            await Assert.That(replayOutbox.Messages.OfType<DispatchRuntime>().Count())
                .IsEqualTo(0);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Changed_source_revision_fails_closed_after_cleanup_without_replay(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);
            var request = Request(fixture);
            _ = await AcquireAsync(options, request, cancellationToken);
            var recovery = await AcquireAsync(options, request, cancellationToken);
            await using (var mutation = new NoCtfDbContext(options))
            {
                var challenge = await mutation.CompetitionChallenges.SingleAsync(
                    item => item.Id == fixture.CompetitionChallengeId,
                    cancellationToken);
                challenge.Revision++;
                await mutation.SaveChangesAsync(cancellationToken);
            }

            var outbox = new RecordingOutbox();
            await using (var replayDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(new ReplayAwdpFixVerification(
                    fixture.GameplayFactId,
                    fixture.RuntimeInstanceId,
                    1,
                    recovery.RuntimeProcessingVersion,
                    fixture.RunnerPool,
                    fixture.RunnerId,
                    fixture.Now.AddSeconds(2)),
                    replayDb,
                    outbox,
                    cancellationToken);
            }

            await using var verification = new NoCtfDbContext(options);
            await Assert.That(await verification.RuntimeInstances.CountAsync(
                runtime => runtime.GameplayFactId == fixture.GameplayFactId,
                cancellationToken)).IsEqualTo(1);
            var fact = await verification.GameplayFacts.AsNoTracking()
                .SingleAsync(item => item.Id == fixture.GameplayFactId, cancellationToken);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.PlatformFailed);
            await Assert.That(fact.FailureCode)
                .IsEqualTo(GameplayFactFailureCode.CheckerPlatformError);
            await Assert.That(outbox.Messages.OfType<DispatchRuntime>()).IsEmpty();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Published_result_wins_before_redelivery_without_creating_a_replacement(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);
            var request = Request(fixture);
            var first = await AcquireAsync(options, request, cancellationToken);

            await using (var stageDb = new NoCtfDbContext(options))
            {
                var advanced = await new PostgresAwdpFixExecutionFence(
                    stageDb,
                    new RecordingOutbox(),
                    TimeProvider.System).TryAdvanceStageAsync(new(
                        fixture.GameplayFactId,
                        fixture.RuntimeInstanceId,
                        1,
                        first.RuntimeProcessingVersion,
                        AwdpFixStage.PatchApplying,
                        AwdpFixStage.CheckerRunning), cancellationToken);
                await Assert.That(advanced).IsTrue();
            }

            await using (var resultDb = new NoCtfDbContext(options))
            {
                var applied = await new InternalResultStore(
                    resultDb,
                    new RecordingOutbox()).RecordAwdpAsync(AwdpFixResult.Create(
                        fixture.GameplayFactId,
                        fixture.RuntimeInstanceId,
                        1,
                        first.RuntimeProcessingVersion,
                        AwdpFixOutcome.DefenseSucceeded,
                        fixture.Now.AddSeconds(1)), cancellationToken);
                await Assert.That(applied).IsEqualTo(InternalResultDisposition.Applied);
            }

            var redelivery = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(redelivery.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Superseded);
            await using var verification = new NoCtfDbContext(options);
            await Assert.That(await verification.RuntimeInstances.CountAsync(
                runtime => runtime.GameplayFactId == fixture.GameplayFactId,
                cancellationToken)).IsEqualTo(1);
            var fact = await verification.GameplayFacts.AsNoTracking()
                .SingleAsync(item => item.Id == fixture.GameplayFactId, cancellationToken);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.Completed);
            await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.Correct);
            var runtime = await verification.RuntimeInstances.AsNoTracking()
                .SingleAsync(item => item.Id == fixture.RuntimeInstanceId, cancellationToken);
            await Assert.That(runtime.AwdpFixStage).IsEqualTo(AwdpFixStage.Completed);
        });
    }

    private static async Task<AwdpFixExecutionFenceResult> AcquireAsync(
        DbContextOptions<NoCtfDbContext> options,
        AwdpFixExecutionFenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PostgresAwdpFixExecutionFence(
            db,
            new RecordingOutbox(),
            TimeProvider.System).AcquireAsync(request, cancellationToken);
    }

    private static AwdpFixExecutionFenceRequest Request(Fixture fixture) =>
        new(
            fixture.GameplayFactId,
            fixture.CompetitionChallengeId,
            fixture.PatchUploadId,
            fixture.RuntimeInstanceId,
            1,
            0,
            fixture.Now.AddMinutes(15),
            fixture.RunnerPool,
            fixture.RunnerId);

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionChallengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        var patchUploadId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        var runnerPool = "tests";
        var runnerId = "runner-1";
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var definition = new AwdpChallengeConfiguration(
            AwdpChallengeConfiguration.CurrentSchemaVersion,
            null,
            null,
            null,
            null,
            null,
            new ChallengeRuntimeTemplate(
                RuntimeAllocation.Shared,
                new ContainerRuntimeDefinition(
                    "target:latest",
                    InternalPorts: [8080]),
                new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 128),
                TtlSeconds: 900,
                OperationTimeoutSeconds: 120),
            Checker: new("checker:latest"));
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP fence",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            ConfigurationRevision = 3,
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Title = "Fence target",
            Visibility = ChallengeVisibility.Private,
            DefinitionJson = JsonSerializer.Serialize(definition, jsonOptions),
            Revision = 5,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 100,
            IsPublished = true,
            RulesJson = new GameModeChallengeConfigurationCatalog()
                .GetDefaultJson(GameMode.Awdp),
            Revision = 7,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            NormalizedName = "TEAM",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        var file = new StoredFile
        {
            Id = fileId,
            ObjectKey = $"fix/{fileId:N}",
            FileName = "fix.tar.gz",
            ContentType = "application/gzip",
            ByteLength = 1,
            Sha256 = RandomNumberGenerator.GetBytes(32),
            CreatedAt = now
        };
        db.Files.Add(file);
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            UploadedByUserId = ownerId,
            RuntimeInstanceId = runtimeId,
            FileId = fileId,
            File = file,
            UploadedAt = now
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = factId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = ownerId,
            Kind = GameplayFactKind.FixAttempt,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = patchUploadId,
            State = GameplayFactState.Processing,
            OccurredAt = now,
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.AwdpTarget,
            GameplayFactId = factId,
            AwdpFixStage = AwdpFixStage.PatchApplying,
            SourceCompetitionConfigurationRevision = 3,
            SourceCompetitionChallengeRevision = 7,
            SourceChallengeDefinitionRevision = 5,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = runnerPool,
            RunnerId = runnerId,
            State = RuntimeState.Running,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now,
            ExpiresAt = now.AddMinutes(15)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionChallengeId,
            factId,
            patchUploadId,
            runtimeId,
            runnerPool,
            runnerId);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_awdp_fence_{Guid.NewGuid():N}")
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

        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionChallengeId,
        Guid GameplayFactId,
        Guid PatchUploadId,
        Guid RuntimeInstanceId,
        string RunnerPool,
        string RunnerId);
}
