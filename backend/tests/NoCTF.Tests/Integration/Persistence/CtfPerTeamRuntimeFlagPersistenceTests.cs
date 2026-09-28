using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CtfPerTeamRuntimeFlagPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Running_runtime_can_extend_before_final_ten_minutes_without_losing_existing_time(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_ctf_runtime_early_extension")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var runtimeId = Guid.CreateVersion7(fixture.Now);
            await using var db = new NoCtfDbContext(options);
            db.RuntimeInstances.Add(new PlayerRuntimeInstance
            {
                Id = runtimeId,
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.StartChallengeId,
                TeamId = fixture.TeamId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Running,
                CreatedAt = fixture.Now.AddMinutes(-1),
                RunningAt = fixture.Now.AddMinutes(-1),
                ExpiresAt = fixture.Now.AddMinutes(52)
            });
            await db.SaveChangesAsync(cancellationToken);
            var store = new RuntimeInstanceStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                new PerTeamRuntimeFlagStore(db),
                new RecordingOutbox());

            var result = await store.MutatePlayerRuntimeAsync(new(
                fixture.CompetitionId,
                fixture.StartChallengeId,
                fixture.UserId,
                RuntimeAction.Extend,
                TimeSpan.FromMinutes(30),
                fixture.Now), cancellationToken);

            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Runtime!.ExpiresAt).IsEqualTo(fixture.Now.AddMinutes(82));
            await Assert.That(await db.RuntimeInstances.AsNoTracking()
                .Where(runtime => runtime.Id == runtimeId)
                .Select(runtime => runtime.ExpiresAt)
                .SingleAsync(cancellationToken))
                .IsEqualTo(fixture.Now.AddMinutes(82));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Start_dispatch_batch_generation_and_reset_reuse_one_fixed_team_flag(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
            var runtimeFlags = new PerTeamRuntimeFlagStore(db);
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
            await Assert.That(initialFlag.Flag).StartsWith("challenge{");

            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await BackendMessageOperations.DispatchRuntimeAsync(
                dispatch,
                db,
                templates,
                new FixedRuntimePlacementPolicy(),
                new FixedCapacityGate("runner-1"),
                outbox,
                TimeProvider.System,
                cancellationToken);
            var claim = outbox.RunnerNodeMessages.OfType<ProvisionContainerRuntime>().Single();
            await Assert.That(claim.Definition.Environment["CHALLENGE_FLAG"])
                .IsEqualTo(initialFlag.Flag);

            var generator = new MissingFlagGenerator(
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
            var batchFlag = await db.ChallengeFlags.AsNoTracking().SingleAsync(
                flag => flag.CompetitionChallengeId == fixture.BatchChallengeId
                    && flag.TeamId == fixture.TeamId
                    && flag.SpecificationKind == SpecificationKind.RuntimeDefinition,
                cancellationToken);
            await Assert.That(batchFlag.Flag).StartsWith("competition{");
            await Assert.That(await db.ChallengeFlags.AsNoTracking().AnyAsync(
                    flag => flag.CompetitionChallengeId == fixture.StaticChallengeId
                        && flag.TeamId == fixture.TeamId,
                    cancellationToken))
                .IsFalse();

            var competition = await db.Competitions.SingleAsync(
                item => item.Id == fixture.CompetitionId,
                cancellationToken);
            competition.ModeConfiguration = TestConfigurations.Competition(
                GameMode.Ctf,
                JsonSerializer.Serialize(
                new CtfConfiguration(
                    new(500, 100, 10),
                    [],
                    FlagTemplate: new("changed", "[TEAMHASH]", false)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var futureUserId = Guid.CreateVersion7();
            var futureTeamId = Guid.CreateVersion7();
            db.Users.Add(new User
            {
                Id = futureUserId,
                UserName = "future-player",
                NormalizedUserName = "FUTURE-PLAYER",
                Email = "future-player@example.test",
                PasswordHash = "test",
                CreatedAt = fixture.Now.AddSeconds(2),
                UpdatedAt = fixture.Now.AddSeconds(2)
            });
            db.Teams.Add(new Team
            {
                Id = futureTeamId,
                CompetitionId = fixture.CompetitionId,
                Name = "Future",
                CaptainId = futureUserId,
                MemberIds = [futureUserId],
                InvitationToken = new string('b', 32),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = fixture.Now.AddSeconds(2)
            });
            await db.SaveChangesAsync(cancellationToken);

            var existingAfterConfigurationChange = await runtimeFlags.EnsureAsync(
                fixture.CompetitionId,
                fixture.BatchChallengeId,
                fixture.TeamId,
                fixture.Now.AddSeconds(2),
                cancellationToken);
            var futureFlag = await runtimeFlags.EnsureAsync(
                fixture.CompetitionId,
                fixture.BatchChallengeId,
                futureTeamId,
                fixture.Now.AddSeconds(2),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await Assert.That(existingAfterConfigurationChange).IsEqualTo(batchFlag.Flag);
            await Assert.That(futureFlag).StartsWith("changed{");

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
            await Assert.That(reset.Runtime!.Id).IsNotEqualTo(started.Runtime!.Id);
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
    public async Task Two_runtimes_are_claimed_by_distinct_runners_and_sent_only_to_node_queues(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runtime_node_routing")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(postgres.StartAsync(cancellationToken),
                nats.StartAsync(cancellationToken));
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var secondUserId = Guid.CreateVersion7();
            var secondTeamId = Guid.CreateVersion7();
            await using var db = new NoCtfDbContext(options);
            db.Users.Add(new User
            {
                Id = secondUserId,
                UserName = "second-player",
                NormalizedUserName = "SECOND-PLAYER",
                Email = "second-player@example.test",
                PasswordHash = "test",
                CreatedAt = fixture.Now,
                UpdatedAt = fixture.Now
            });
            db.Teams.Add(new Team
            {
                Id = secondTeamId,
                CompetitionId = fixture.CompetitionId,
                Name = "Second",
                CaptainId = secondUserId,
                MemberIds = [secondUserId],
                InvitationToken = new string('c', 32),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = fixture.Now
            });
            await db.SaveChangesAsync(cancellationToken);

            var templates = new ChallengeRuntimeTemplateCatalog();
            var outbox = new RecordingOutbox();
            var runtimes = new RuntimeInstanceStore(
                db,
                templates,
                new FixedRuntimePlacementPolicy(),
                new PerTeamRuntimeFlagStore(db),
                outbox);
            var first = await runtimes.MutatePlayerRuntimeAsync(
                new(
                    fixture.CompetitionId,
                    fixture.StartChallengeId,
                    fixture.UserId,
                    RuntimeAction.Start,
                    null,
                    fixture.Now),
                cancellationToken);
            var second = await runtimes.MutatePlayerRuntimeAsync(
                new(
                    fixture.CompetitionId,
                    fixture.StartChallengeId,
                    secondUserId,
                    RuntimeAction.Start,
                    null,
                    fixture.Now),
                cancellationToken);
            await Assert.That(first.Failure).IsNull();
            await Assert.That(second.Failure).IsNull();

            var registry = new NatsRunnerAvailabilityRegistry(connection, TimeProvider.System);
            await using var ownerA = await new NatsClusterLeaseManager(connection)
                .TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner-a"),
                    "runner-a", cancellationToken);
            await using var ownerB = await new NatsClusterLeaseManager(connection)
                .TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner-b"),
                    "runner-b", cancellationToken);
            await Assert.That(ownerA).IsNotNull();
            await Assert.That(ownerB).IsNotNull();
            var runnerCapacity = new RuntimeResourceLimits(
                512 * 1024 * 1024,
                500_000_000,
                256);
            foreach (var runnerId in new[] { "runner-a", "runner-b" })
            {
                await registry.PublishHeartbeatAsync("tests", runnerId,
                    RuntimeProvider.Docker, TimeSpan.FromMinutes(2), cancellationToken);
                var registered = await registry.RegisterAsync(
                    CurrentRunnerRegistration.Create(
                        "tests",
                        runnerId,
                        new(
                            runnerCapacity.MemoryBytes,
                            runnerCapacity.NanoCpus,
                            runnerCapacity.PidsLimit),
                        timeToLive: TimeSpan.FromMinutes(2),
                        resourceDomainFencingToken: runnerId == "runner-a"
                            ? ownerA!.FencingToken : ownerB!.FencingToken),
                    cancellationToken);
                await Assert.That(registered)
                    .IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            }

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels).Services.BuildServiceProvider();
            var capacity = new PersistedRunnerCapacityGate(db, registry,
                cacheServices.GetRequiredService<IFusionCacheProvider>(), outbox,
                new RunnerCapacityLedgerCoordinator());
            var dispatches = outbox.Published.OfType<DispatchRuntime>().ToArray();
            await Assert.That(dispatches).Count().IsEqualTo(2);
            foreach (var dispatch in dispatches)
            {
                await BackendMessageOperations.DispatchRuntimeAsync(
                    dispatch,
                    db,
                    templates,
                    new FixedRuntimePlacementPolicy(),
                    capacity,
                    outbox,
                    TimeProvider.System,
                    cancellationToken);
            }

            var provisions = outbox.RunnerNodeMessages
                .OfType<ProvisionContainerRuntime>()
                .ToArray();
            await Assert.That(provisions).Count().IsEqualTo(2);
            await Assert.That(provisions.Select(item => item.RunnerId).Distinct())
                .Count()
                .IsEqualTo(2);
            await Assert.That(provisions
                    .Select(item => RunnerNodeQueueName.FromRunnerId(item.RunnerId))
                    .Distinct())
                .Count()
                .IsEqualTo(2);
            var assignments = await db.RuntimeInstances.AsNoTracking()
                .Where(item => item.Id == first.Runtime!.Id || item.Id == second.Runtime!.Id)
                .Select(item => new { item.Id, item.RunnerId, item.State })
                .ToArrayAsync(cancellationToken);
            await Assert.That(assignments.Select(item => item.RunnerId).Distinct())
                .Count()
                .IsEqualTo(2);
            await Assert.That(assignments.All(item => item.State == RuntimeState.Provisioning))
                .IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
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
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new CtfCompetition
        {
            Id = competitionId,
            Title = "CTF runtime flags",
            OwnerId = userId,
            Status = CompetitionStatus.Running,
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
            ModeConfiguration = TestConfigurations.Competition(
                GameMode.Ctf,
                JsonSerializer.Serialize(
                new CtfConfiguration(
                    new(500, 100, 10),
                    [],
                    FlagTemplate: new("competition", "[TEAMHASH]", false)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Team",
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
                Security: new(false, false, false, ["ALL"], []),
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
            new("challenge", "[TEAMHASH]", false),
            now);
        AddChallenge(
            db,
            userId,
            competitionId,
            batchChallengeId,
            "Batch",
            2,
            RuntimeConfiguration(perTeamRuntime),
            null,
            now);
        AddChallenge(
            db,
            userId,
            competitionId,
            staticChallengeId,
            "Static",
            3,
            RuntimeConfiguration(staticRuntime),
            null,
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
        string configurationFixture,
        NoCTF.GameModes.Flags.PerTeamFlagTemplate? flagTemplate,
        DateTimeOffset now)
    {
        var challengeId = Guid.CreateVersion7(now);
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = title,
            Definition = TestConfigurations.Definition(GameMode.Ctf, configurationFixture),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Order = order,
            IsPublished = true,
            Rules = TestConfigurations.Rules(
                GameMode.Ctf,
                JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    null,
                    null,
                    FlagTemplate: flagTemplate),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            UpdatedAt = now
        });
    }

    private static string RuntimeConfiguration(ChallengeRuntimeTemplate runtime) =>
        JsonSerializer.Serialize(
            new CtfChallengeConfiguration(
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

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class FixedCapacityGate(string runnerId) : IRunnerCapacityGate
    {
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
            CancellationToken cancellationToken) =>
            Task.FromResult(new RunnerCapacityClaim(
                RunnerCapacityAvailability.Claimed,
                runnerId,
                RunnerCapacityClaimState.Acquired));

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
