using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.PatchUploads;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("AwdpDefenseTarget")]
public sealed class AwdpDefenseTargetPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_requests_and_uploads_create_one_target_and_consume_it_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);

            var requests = await Task.WhenAll(
                RequestTargetAsync(options, fixture, cancellationToken),
                RequestTargetAsync(options, fixture, cancellationToken));
            await Assert.That(requests.Select(result => result.State))
                .Contains(AwdpDefenseTargetRequestState.Created);
            await Assert.That(requests.Count(result =>
                    result.State == AwdpDefenseTargetRequestState.Created))
                .IsEqualTo(1);
            await Assert.That(requests.Count(result =>
                    result.State == AwdpDefenseTargetRequestState.ActiveTargetExists))
                .IsEqualTo(1);
            var runtimeId = requests.Single(result =>
                result.State == AwdpDefenseTargetRequestState.Created).RuntimeInstanceId!.Value;

            await using (var provisioned = new NoCtfDbContext(options))
            {
                var runtime = await provisioned.RuntimeInstances.SingleAsync(
                    item => item.Id == runtimeId,
                    cancellationToken);
                runtime.State = RuntimeState.Running;
                runtime.RunnerId = "runner-1";
                runtime.ProviderReceiptJson = "{}";
                runtime.RunningAt = fixture.Now;
                runtime.ExpiresAt = fixture.Now.AddMinutes(15);
                runtime.AwdpFixStage = AwdpFixStage.AwaitingPatch;
                await provisioned.SaveChangesAsync(cancellationToken);
            }

            var firstFileId = Guid.CreateVersion7();
            var secondFileId = Guid.CreateVersion7();
            await AddFilesAsync(
                options,
                fixture.Now,
                [firstFileId, secondFileId],
                cancellationToken);
            var saves = await Task.WhenAll(
                SavePatchAsync(
                    options,
                    fixture,
                    runtimeId,
                    firstFileId,
                    cancellationToken),
                SavePatchAsync(
                    options,
                    fixture,
                    runtimeId,
                    secondFileId,
                    cancellationToken));

            await Assert.That(saves.Count(result =>
                    result.Result.State == PatchUploadSaveState.Accepted))
                .IsEqualTo(1);
            await Assert.That(saves.Count(result =>
                    result.Result.State == PatchUploadSaveState.DefenseTargetConsumed))
                .IsEqualTo(1);
            await Assert.That(saves.Sum(result => result.RunMessages)).IsEqualTo(1);

            await using var verification = new NoCtfDbContext(options);
            await Assert.That(await verification.PatchUploads.CountAsync(
                upload => upload.RuntimeInstanceId == runtimeId,
                cancellationToken)).IsEqualTo(1);
            await Assert.That(await verification.GameplayFacts.CountAsync(
                fact => fact.Kind == GameplayFactKind.FixAttempt,
                cancellationToken)).IsEqualTo(1);
            var publicFixEvent = await verification.CompetitionEvents.AsNoTracking()
                .SingleAsync(item => item.Kind == CompetitionEventKind.AwdpFixAttempted,
                    cancellationToken);
            await Assert.That(publicFixEvent.Visibility)
                .IsEqualTo(CompetitionEventVisibility.Public);
            await Assert.That(publicFixEvent.TeamId).IsEqualTo(fixture.TeamId);
            await Assert.That(publicFixEvent.CompetitionChallengeId)
                .IsEqualTo(fixture.CompetitionChallengeId);
            await Assert.That(publicFixEvent.ActorUserId).IsNull();
            var target = await verification.RuntimeInstances.AsNoTracking()
                .SingleAsync(item => item.Id == runtimeId, cancellationToken);
            await Assert.That(target.GameplayFactId).IsNotNull();
            await Assert.That(target.AwdpFixStage).IsEqualTo(AwdpFixStage.PatchApplying);
        });
    }

    private static async Task<AwdpDefenseTargetRequestResult> RequestTargetAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var store = new AwdpDefenseTargetStore(
            db,
            new(new()),
            new FixedRuntimePlacementPolicy(),
            new RecordingOutbox());
        return await store.TryCreateAsync(
            fixture.CompetitionId,
            fixture.CompetitionChallengeId,
            fixture.UserId,
            fixture.Now,
            cancellationToken);
    }

    private static async Task<(PatchUploadSaveResult Result, int RunMessages)> SavePatchAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        Guid runtimeId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var outbox = new RecordingOutbox();
        var store = new PatchUploadStore(
            db,
            outbox,
            new GameplayFactAttemptCriticalSection(new LocalCriticalSectionRegistry()),
            new FileReferenceLock(),
            NullLogger<PatchUploadStore>.Instance,
            new CompetitionEventStore(db, outbox));
        var scope = await store.ResolveScopeAsync(
            fixture.CompetitionId,
            fixture.CompetitionChallengeId,
            runtimeId,
            fixture.UserId,
            cancellationToken);
        var result = await store.SaveAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            scope!,
            fileId,
            fixture.Now.AddSeconds(1),
            cancellationToken);
        return (result, outbox.Messages.OfType<RunAwdpFixVerification>().Count());
    }

    private static async Task AddFilesAsync(
        DbContextOptions<NoCtfDbContext> options,
        DateTimeOffset now,
        IReadOnlyList<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        foreach (var fileId in fileIds)
        {
            db.Files.Add(new StoredFile
            {
                Id = fileId,
                ObjectKey = $"fix/{fileId:N}",
                FileName = "fix.tar.gz",
                ContentType = "application/gzip",
                ByteLength = 1,
                Sha256 = RandomNumberGenerator.GetBytes(32),
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var definition = new AwdpChallengeConfiguration(
            AwdpChallengeConfiguration.CurrentSchemaVersion,
            Break: null,
            Fix: null,
            RequireBreakBeforeFix: false,
            MaxBreakSubmissions: null,
            MaxFixSubmissions: null,
            Runtime: new(
                RuntimeAllocation.PerTeam,
                new ContainerRuntimeDefinition(
                    "target:test",
                    PortMappings: new Dictionary<int, int> { [8080] = 0 },
                    FlagEnvironmentVariableName: "FLAG",
                    InternalPorts: [8080]),
                new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 128),
                TtlSeconds: 900,
                OperationTimeoutSeconds: 120,
                UrlBindings:
                [
                    new RuntimeUrlBinding(
                        "http://{HOST}:{PORT}",
                        RuntimeExposure.OwnerOnly,
                        ContainerPort: 8080)
                ],
                FlagSource: RuntimeFlagSource.PerTeam),
            Checker: new("checker:test"));
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "participant",
            NormalizedUserName = "PARTICIPANT",
            Email = "participant@example.test",
            NormalizedEmail = "PARTICIPANT@EXAMPLE.TEST",
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
            Title = "AWDP defense target",
            OwnerId = userId,
            Mode = GameMode.Awdp,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            ConfigurationRevision = 1,
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
            OwnerId = userId,
            Mode = GameMode.Awdp,
            Title = "Defense target",
            Visibility = ChallengeVisibility.Private,
            DefinitionJson = JsonSerializer.Serialize(definition, JsonOptions),
            Revision = 1,
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
            Revision = 1,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            NormalizedName = "TEAM",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.Player,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "test-pool",
            RunnerId = "attack-runner",
            State = RuntimeState.Running,
            ProviderReceiptJson = "{}",
            SourceCompetitionConfigurationRevision = 1,
            SourceCompetitionChallengeRevision = 1,
            SourceChallengeDefinitionRevision = 1,
            CreatedAt = now,
            RunningAt = now,
            ExpiresAt = now.AddMinutes(15)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, competitionChallengeId, userId, teamId);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_awdp_target_{Guid.NewGuid():N}")
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
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage => Add(message);
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => Add(message);
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => Add(message);
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => Add(message);
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;

        private ValueTask Add<T>(T message)
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid UserId,
        Guid TeamId);
}
