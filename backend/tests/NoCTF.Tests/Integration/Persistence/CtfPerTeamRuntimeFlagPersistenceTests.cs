using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CtfPerTeamRuntimeFlagPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Start_dispatch_batch_generation_and_reset_reuse_one_fixed_team_flag(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_ctf_runtime_flags")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var templates = new ChallengeRuntimeTemplateCatalog();
            var outbox = new RecordingOutbox();

            await using var db = new NoCtfDbContext(options);
            var runtimeFlags = new PostgresPerTeamRuntimeFlagStore(db);
            var runtimes = new RuntimeInstanceStore(
                db,
                templates,
                new FixedRuntimePlacementPolicy(),
                runtimeFlags,
                outbox);

            var started = await runtimes.MutatePlayerRuntimeAsync(
                new(
                    fixture.CompetitionId,
                    fixture.StartChallengeId,
                    fixture.UserId,
                    RuntimeAction.Start,
                    null,
                    fixture.Now),
                cancellationToken);

            await Assert.That(started.Failure).IsNull();
            await Assert.That(started.Runtime).IsNotNull();
            var initialFlag = await db.ChallengeFlags.AsNoTracking().SingleAsync(
                flag => flag.CompetitionChallengeId == fixture.StartChallengeId
                    && flag.TeamId == fixture.TeamId
                    && flag.SpecificationKind == SpecificationKind.RuntimeDefinition
                    && flag.SpecificationId == fixture.StartChallengeId,
                cancellationToken);

            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await BackendMessageHandlers.Handle(
                dispatch,
                db,
                templates,
                new PostgresRuntimePublishedPortAllocator(
                    db,
                    new RuntimePublishedPortRange()),
                outbox,
                cancellationToken);
            var claim = outbox.RunnerPoolMessages.OfType<ClaimContainerRuntime>().Single();
            await Assert.That(claim.Definition.Environment["CHALLENGE_FLAG"])
                .IsEqualTo(initialFlag.Flag);

            var generator = new PostgresMissingFlagGenerator(
                db,
                templates,
                runtimeFlags);
            var failures = await generator.GenerateAsync(
                fixture.CompetitionId,
                fixture.Now.AddSeconds(1),
                cancellationToken);

            await Assert.That(failures).IsEmpty();
            await Assert.That(await db.ChallengeFlags.AsNoTracking().CountAsync(
                    flag => flag.TeamId == fixture.TeamId
                        && flag.SpecificationKind == SpecificationKind.RuntimeDefinition,
                    cancellationToken))
                .IsEqualTo(2);
            await Assert.That(await db.ChallengeFlags.AsNoTracking().AnyAsync(
                    flag => flag.CompetitionChallengeId == fixture.BatchChallengeId
                        && flag.TeamId == fixture.TeamId
                        && flag.SpecificationKind == SpecificationKind.RuntimeDefinition,
                    cancellationToken))
                .IsTrue();
            await Assert.That(await db.ChallengeFlags.AsNoTracking().AnyAsync(
                    flag => flag.CompetitionChallengeId == fixture.StaticChallengeId
                        && flag.TeamId == fixture.TeamId,
                    cancellationToken))
                .IsFalse();

            var reset = await runtimes.MutatePlayerRuntimeAsync(
                new(
                    fixture.CompetitionId,
                    fixture.StartChallengeId,
                    fixture.UserId,
                    RuntimeAction.Reset,
                    null,
                    fixture.Now.AddSeconds(2)),
                cancellationToken);

            await Assert.That(reset.Failure).IsNull();
            await Assert.That(reset.Runtime).IsNotNull();
            await Assert.That(reset.Runtime!.Generation).IsEqualTo(2);
            var resetFlags = await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.CompetitionChallengeId == fixture.StartChallengeId
                    && flag.TeamId == fixture.TeamId
                    && flag.SpecificationKind == SpecificationKind.RuntimeDefinition)
                .Select(flag => flag.Flag)
                .ToArrayAsync(cancellationToken);
            await Assert.That(resetFlags).IsEquivalentTo([initialFlag.Flag]);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Provisioning_fallback_dispatches_only_roots_or_replacements_with_stopped_predecessors(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_ctf_provisioning_fence")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var additionalTeamIds = Enumerable.Range(0, 4)
                .Select(_ => Guid.CreateVersion7())
                .ToArray();
            var rootId = Guid.CreateVersion7();
            var stoppedPredecessorId = Guid.CreateVersion7();
            var stoppedWaiterId = Guid.CreateVersion7();
            var stoppingPredecessorId = Guid.CreateVersion7();
            var stoppingWaiterId = Guid.CreateVersion7();
            var failedPredecessorId = Guid.CreateVersion7();
            var failedWaiterId = Guid.CreateVersion7();
            var missingPredecessorId = Guid.CreateVersion7();
            var missingWaiterId = Guid.CreateVersion7();

            await using (var arrange = new NoCtfDbContext(options))
            {
                arrange.Teams.AddRange(additionalTeamIds.Select((teamId, index) => new Team
                {
                    Id = teamId,
                    CompetitionId = fixture.CompetitionId,
                    Name = $"Fence {index}",
                    NormalizedName = $"FENCE {index}",
                    CaptainId = fixture.UserId,
                    MemberIds = [fixture.UserId],
                    InvitationToken = teamId.ToString("N"),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = fixture.Now
                }));
                arrange.RuntimeInstances.AddRange(
                    Runtime(rootId, fixture, fixture.TeamId, 1, RuntimeState.Queued),
                    Runtime(
                        stoppedPredecessorId,
                        fixture,
                        additionalTeamIds[0],
                        1,
                        RuntimeState.Stopped),
                    Runtime(
                        stoppedWaiterId,
                        fixture,
                        additionalTeamIds[0],
                        2,
                        RuntimeState.Queued,
                        stoppedPredecessorId),
                    Runtime(
                        stoppingPredecessorId,
                        fixture,
                        additionalTeamIds[1],
                        1,
                        RuntimeState.Stopping),
                    Runtime(
                        stoppingWaiterId,
                        fixture,
                        additionalTeamIds[1],
                        2,
                        RuntimeState.Queued,
                        stoppingPredecessorId),
                    Runtime(
                        failedPredecessorId,
                        fixture,
                        additionalTeamIds[2],
                        1,
                        RuntimeState.Failed,
                        hasReceipt: true),
                    Runtime(
                        failedWaiterId,
                        fixture,
                        additionalTeamIds[2],
                        2,
                        RuntimeState.Queued,
                        failedPredecessorId),
                    Runtime(
                        missingPredecessorId,
                        fixture,
                        additionalTeamIds[3],
                        1,
                        RuntimeState.Stopped),
                    Runtime(
                        missingWaiterId,
                        fixture,
                        additionalTeamIds[3],
                        2,
                        RuntimeState.Queued,
                        missingPredecessorId));
                await arrange.SaveChangesAsync(cancellationToken);

                await arrange.Database.OpenConnectionAsync(cancellationToken);
                try
                {
                    await arrange.Database.ExecuteSqlRawAsync(
                        "SET session_replication_role = replica",
                        cancellationToken);
                    await arrange.RuntimeInstances
                        .Where(runtime => runtime.Id == missingPredecessorId)
                        .ExecuteDeleteAsync(cancellationToken);
                }
                finally
                {
                    await arrange.Database.ExecuteSqlRawAsync(
                        "SET session_replication_role = origin",
                        cancellationToken);
                    await arrange.Database.CloseConnectionAsync();
                }
            }

            var outbox = new RecordingOutbox();
            await using var db = new NoCtfDbContext(options);
            await BackendMessageHandlers.Handle(
                new ProvisionCompetitionRuntimes(fixture.CompetitionId),
                new FixedAwdProvisioner(AwdRuntimeProvisioningOutcome.NotApplicable),
                new FixedKohProvisioner(KohRuntimeProvisioningOutcome.NotApplicable),
                db,
                outbox,
                cancellationToken);

            await Assert.That(outbox.Published.OfType<DispatchRuntime>()
                    .Select(message => message.RuntimeInstanceId))
                .IsEquivalentTo([rootId, stoppedWaiterId]);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()
                    .Any(message => message.RuntimeInstanceId == stoppingWaiterId
                        || message.RuntimeInstanceId == failedWaiterId
                        || message.RuntimeInstanceId == missingWaiterId))
                .IsFalse();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now);
        var teamId = Guid.CreateVersion7(now);
        var startChallengeId = Guid.CreateVersion7(now);
        var batchChallengeId = Guid.CreateVersion7(now);
        var staticChallengeId = Guid.CreateVersion7(now);
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "player",
            NormalizedUserName = "PLAYER",
            Email = "player@example.test",
            NormalizedEmail = "PLAYER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "CTF runtime flags",
            OwnerId = userId,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Team",
            NormalizedName = "TEAM",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });

        var perTeamRuntime = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                Environment: new Dictionary<string, string>
                {
                    ["CHALLENGE_FLAG"] = "author-value"
                },
                FlagEnvironmentVariableName: "CHALLENGE_FLAG"),
            Limits: new(268_435_456, 500_000_000, 128),
            FlagSource: RuntimeFlagSource.PerTeam);
        var staticRuntime = perTeamRuntime with
        {
            Definition = ((ContainerRuntimeDefinition)perTeamRuntime.Definition) with
            {
                FlagEnvironmentVariableName = null
            },
            FlagSource = RuntimeFlagSource.Static
        };
        AddChallenge(
            db,
            userId,
            competitionId,
            startChallengeId,
            "Start",
            1,
            RuntimeConfiguration(perTeamRuntime),
            now);
        AddChallenge(
            db,
            userId,
            competitionId,
            batchChallengeId,
            "Batch",
            2,
            RuntimeConfiguration(perTeamRuntime),
            now);
        AddChallenge(
            db,
            userId,
            competitionId,
            staticChallengeId,
            "Static",
            3,
            RuntimeConfiguration(staticRuntime),
            now);
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            userId,
            competitionId,
            teamId,
            startChallengeId,
            batchChallengeId,
            staticChallengeId);
    }

    private static void AddChallenge(
        NoCtfDbContext db,
        Guid ownerId,
        Guid competitionId,
        Guid competitionChallengeId,
        string title,
        int order,
        string configurationJson,
        DateTimeOffset now)
    {
        var challengeId = Guid.CreateVersion7(now);
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = title,
            DefinitionJson = configurationJson,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Order = order,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        });
    }

    private static string RuntimeConfiguration(ChallengeRuntimeTemplate runtime) =>
        JsonSerializer.Serialize(
            new CtfChallengeConfiguration(
                CtfChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                Runtime: runtime),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid UserId,
        Guid CompetitionId,
        Guid TeamId,
        Guid StartChallengeId,
        Guid BatchChallengeId,
        Guid StaticChallengeId);

    private static RuntimeInstance Runtime(
        Guid id,
        Fixture fixture,
        Guid teamId,
        int generation,
        RuntimeState state,
        Guid? replacesRuntimeInstanceId = null,
        bool hasReceipt = false) => new()
        {
            Id = id,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.StartChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.Player,
            Generation = generation,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "default",
            RunnerId = hasReceipt ? "runner-a" : null,
            State = state,
            FailureCode = state == RuntimeState.Failed
                ? RuntimeFailureCode.CleanupFailed
                : null,
            ReplacesRuntimeInstanceId = replacesRuntimeInstanceId,
            ProviderReceiptJson = hasReceipt ? "{}" : null,
            CreatedAt = fixture.Now
        };

    private sealed class FixedAwdProvisioner(AwdRuntimeProvisioningOutcome outcome)
        : IAwdRuntimeProvisioner
    {
        public Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(outcome);
    }

    private sealed class FixedKohProvisioner(KohRuntimeProvisioningOutcome outcome)
        : IKohRuntimeProvisioner
    {
        public Task<KohRuntimeProvisioningOutcome> EnsureAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(outcome);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerPoolMessages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            throw new NotSupportedException();

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage
        {
            RunnerPoolMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerPoolAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage =>
            throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
