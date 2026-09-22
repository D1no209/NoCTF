using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.PatchVerification;
using NoCTF.Infrastructure.GameplayFacts.PatchUploads;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CtfPatchVerification")]
public sealed class CtfPatchVerificationTargetPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Same_challenge_player_and_verification_target_share_one_business_slot(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            var quota = new TeamRuntimeQuota(new());
            var store = new PatchVerificationTargetStore(
                db,
                new GameplayFactAttemptCriticalSection(new()),
                new FixedRuntimePlacementPolicy(),
                quota,
                outbox);

            var created = await store.TryCreateAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                fixture.Now,
                ct);
            var duplicate = await store.TryCreateAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                fixture.Now.AddSeconds(1),
                ct);
            var state = await store.GetStateAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                ct);
            var target = await db.RuntimeInstances.SingleAsync(instance =>
                instance.Id == created.RuntimeInstanceId, ct);
            target.State = RuntimeState.Running;
            target.RunnerId = "runner-1";
            target.ProviderReceiptJson = "{}";
            target.RunningAt = fixture.Now;
            target.ExpiresAt = fixture.Now.AddMinutes(10);
            await db.SaveChangesAsync(ct);
            var uploadStore = new PatchUploadStore(
                db,
                outbox,
                NullLogger<PatchUploadStore>.Instance);
            var uploadScope = await uploadStore.ResolveScopeAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                target.Id,
                fixture.UserId,
                ct);

            await Assert.That(created.State)
                .IsEqualTo(PatchVerificationTargetRequestState.Created);
            await Assert.That(duplicate.State)
                .IsEqualTo(PatchVerificationTargetRequestState.ActiveTargetExists);
            await Assert.That(outbox.Messages.OfType<DispatchRuntime>()).HasSingleItem();
            await Assert.That(state).IsNotNull();
            await Assert.That(state!.MaximumAttempts).IsEqualTo(2);
            await Assert.That(state.RemainingAttempts).IsEqualTo(2);
            await Assert.That(uploadScope).IsNotNull();
            await Assert.That(uploadScope!.MaximumArchiveBytes)
                .IsEqualTo(64L * 1024 * 1024);
            await Assert.That(uploadScope.Purpose)
                .IsEqualTo(RuntimePurpose.PatchVerificationTarget);
            await Assert.That(await db.RuntimeInstances.CountAsync(instance =>
                instance.CompetitionChallengeId == fixture.CompetitionChallengeId
                && (instance.Purpose == RuntimePurpose.Player
                    || instance.Purpose == RuntimePurpose.PatchVerificationTarget), ct))
                .IsEqualTo(2);
            await Assert.That(await quota.CanCreateSlotAsync(
                db,
                fixture.CompetitionId,
                fixture.TeamId,
                Guid.NewGuid(),
                1,
                ct)).IsFalse();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "ctf-patch-target:test",
                PortMappings: new Dictionary<int, int> { [8080] = 0 },
                Security: new(false, false, false, ["ALL"], []),
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
            FlagSource: RuntimeFlagSource.Static);
        var definition = new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            null,
            null,
            Runtime: runtime,
            InteractionKind: CtfInteractionKind.PatchVerification,
            PatchEntrypoint: "fix.sh",
            PatchCommand: ["/bin/sh", "{entrypoint}"],
            PatchTimeoutSeconds: 60,
            Checker: new("ctf-patch-checker:test", TimeoutSeconds: 60),
            ReadyTimeoutSeconds: 30,
            MaximumPatchUploadBytes: 64L * 1024 * 1024);
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "patch-player",
            NormalizedUserName = "PATCH-PLAYER",
            Email = "patch@example.test",
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
            Title = "CTF Patch Verification",
            OwnerId = userId,
            Mode = GameMode.Ctf,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            MaxConcurrentRuntimeInstancesPerTeam = 1,
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
            Mode = GameMode.Ctf,
            Title = "Patch target",
            Direction = "Pwn",
            Visibility = ChallengeVisibility.Private,
            DefinitionJson = JsonSerializer.Serialize(definition, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(new CtfChallengeConfiguration(
                CtfConfiguration.CurrentSchemaVersion,
                null,
                null,
                MaxPatchAttempts: 2), JsonOptions),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Patchers",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('p', 32),
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
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerId = "runner-1",
            State = RuntimeState.Running,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now,
            ExpiresAt = now.AddMinutes(15)
        });
        await db.SaveChangesAsync(ct);
        return new(now, competitionId, competitionChallengeId, userId, teamId);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken ct)
    {
        var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_ctf_patch_{Guid.NewGuid():N}")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(ct);
        return postgres;
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            PublishAsync(message);

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            PublishAsync(message);

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => PublishAsync(message);

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid UserId,
        Guid TeamId);
}
