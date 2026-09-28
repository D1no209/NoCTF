using System.Security.Cryptography;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.GameplayFact;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Scoring;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[NotInParallel]
public sealed class CompetitionPracticeModePersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Challenge_attempt_state_uses_fewer_commands_than_full_admission(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_attempt_state_read");
            await postgres.StartAsync(cancellationToken);
            var fixture = await SeedAsync(Options(postgres), earlyFinish: true, cancellationToken);
            var counter = new QueryCounter();
            var measuredOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Throw(
                    RelationalEventId.MultipleCollectionIncludeWarning))
                .AddInterceptors(counter)
                .Options;
            await using var db = new NoCtfDbContext(measuredOptions);
            var full = await GameplayFactAdmissionPersistence.LoadAsync(
                db, fixture.CompetitionId, fixture.CompetitionChallengeId,
                fixture.UserId, cancellationToken);
            var fullCommands = counter.ReaderCount;
            var state = await new FlagAttemptStateReader(db).ReadAsync(
                fixture.CompetitionId, fixture.CompetitionChallengeId,
                fixture.TeamId, GameMode.Ctf, CompetitionStatus.Finished,
                cancellationToken);
            var narrowCommands = counter.ReaderCount - fullCommands;

            await Assert.That(full).IsNotNull();
            await Assert.That(state).IsEqualTo(new FlagAttemptState(null, 0, null, false));
            await Assert.That(narrowCommands).IsLessThan(fullCommands);
            await Assert.That(narrowCommands).IsLessThanOrEqualTo(4);

            var samples = new List<string> { "iteration,full_ms,narrow_ms" };
            var fullDurations = new List<double>();
            var narrowDurations = new List<double>();
            for (var iteration = 0; iteration < 50; iteration++)
            {
                await using var requestDb = new NoCtfDbContext(Options(postgres));
                var started = Stopwatch.GetTimestamp();
                await GameplayFactAdmissionPersistence.LoadAsync(
                    requestDb, fixture.CompetitionId, fixture.CompetitionChallengeId,
                    fixture.UserId, cancellationToken);
                var fullMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started = Stopwatch.GetTimestamp();
                await new FlagAttemptStateReader(requestDb).ReadAsync(
                    fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.TeamId,
                    GameMode.Ctf, CompetitionStatus.Finished, cancellationToken);
                var narrowMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                fullDurations.Add(fullMs);
                narrowDurations.Add(narrowMs);
                samples.Add($"{iteration + 1},{fullMs.ToString(CultureInfo.InvariantCulture)},{narrowMs.ToString(CultureInfo.InvariantCulture)}");
            }
            var artifactDirectory = Path.Combine(Environment.CurrentDirectory,
                "TestResults", "performance");
            Directory.CreateDirectory(artifactDirectory);
            await File.WriteAllLinesAsync(Path.Combine(artifactDirectory,
                "challenge-attempt-state-current.csv"), samples, cancellationToken);
            Console.WriteLine($"ChallengeAttemptState full p95={Percentile(fullDurations):F2}ms "
                + $"narrow p95={Percentile(narrowDurations):F2}ms "
                + $"commands {fullCommands}->{narrowCommands}");
        });
    }

    private static double Percentile(IReadOnlyList<double> samples) =>
        samples.Order().ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return ValueTask.FromResult(result);
        }
    }

    [Test, Timeout(300_000)]
    public async Task Flag_recheck_rejects_rules_changed_after_initial_admission(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_flag_rules_recheck");
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, earlyFinish: false, cancellationToken);
            await using (var setup = new NoCtfDbContext(options))
            {
                var competition = await setup.Competitions.AsSplitQuery().SingleAsync(
                    item => item.Id == fixture.CompetitionId, cancellationToken);
                competition.Status = CompetitionStatus.Running;
                competition.EndAt = fixture.Now.AddHours(1);
                competition.PracticeModeEnabled = false;
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using var intakeDb = new NoCtfDbContext(options);
            var visibleAttempts = await new FlagAttemptStateReader(intakeDb).ReadAsync(
                fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.TeamId,
                GameMode.Ctf, CompetitionStatus.Running, cancellationToken);
            await Assert.That(visibleAttempts)
                .IsEqualTo(new FlagAttemptState(1, 1, 0, true));
            var intake = new GameplayFactIntakeStore(
                intakeDb, new RecordingOutbox(), new GameplayFactAttemptCriticalSection());
            var admission = await intake.LoadAdmissionAsync(
                fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.UserId,
                cancellationToken);
            await Assert.That(admission).IsNotNull();
            await Assert.That(admission!.ChallengeRules.MaxFlagAttempts).IsEqualTo(1);
            var rootStampBefore = await intakeDb.CompetitionChallenges.AsNoTracking()
                .Where(item => item.Id == fixture.CompetitionChallengeId)
                .Select(item => item.ConcurrencyStamp)
                .SingleAsync(cancellationToken);

            await using (var update = new NoCtfDbContext(options))
            {
                var challenge = await update.CompetitionChallenges.AsSplitQuery().SingleAsync(
                    item => item.Id == fixture.CompetitionChallengeId, cancellationToken);
                ((CtfCompetitionChallengeRules)challenge.Rules!).MaxFlagAttempts = 2;
                await update.SaveChangesAsync(cancellationToken);
            }

            var rootStampAfter = await intakeDb.CompetitionChallenges.AsNoTracking()
                .Where(item => item.Id == fixture.CompetitionChallengeId)
                .Select(item => item.ConcurrencyStamp)
                .SingleAsync(cancellationToken);
            await Assert.That(rootStampAfter).IsEqualTo(rootStampBefore);

            var factCountBefore = await intakeDb.GameplayFacts.CountAsync(cancellationToken);
            var result = await intake.TryAcceptFlagAsync(new(
                Guid.CreateVersion7(fixture.Now), fixture.CompetitionId, fixture.TeamId,
                fixture.CompetitionChallengeId, fixture.UserId, GameplayFactKind.FlagAttempt,
                fixture.Flag, SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Flag)), fixture.Now),
                admission, maxAttempts: 1, cancellationToken);
            await Assert.That(result.State).IsEqualTo(GameplayFactAcceptanceState.AdmissionRejected);
            await Assert.That(await intakeDb.GameplayFacts.CountAsync(cancellationToken)).IsEqualTo(factCountBefore);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Standard_flag_intake_records_independent_unlimited_practice_without_changing_official_results(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_practice_facts");
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, earlyFinish: true, cancellationToken);
            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            var events = new CompetitionEventStore(db, outbox);
            var clock = new FakeTimeProvider(fixture.Now);
            var intake = Intake(db, outbox, events, clock);
            var submit = new SubmitFlag(intake, new GameModeGameplayFactAdmissionPolicy());

            var wrong = await submit.ExecuteAsync(new(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                "flag{wrong}",
                fixture.Now), cancellationToken);
            await Assert.That(wrong.Succeeded).IsTrue();
            await ProcessAsync(db, outbox, events, wrong.Value!.GameplayFactId, cancellationToken);

            clock.SetUtcNow(fixture.Now.AddSeconds(1));
            var correct = await submit.ExecuteAsync(new(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                fixture.Flag,
                clock.GetUtcNow()), cancellationToken);
            await Assert.That(correct.Succeeded).IsTrue();
            await ProcessAsync(db, outbox, events, correct.Value!.GameplayFactId, cancellationToken);

            var practiceFacts = await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.OccurredAt >= fixture.FinishedAt)
                .OrderBy(fact => fact.OccurredAt)
                .ToArrayAsync(cancellationToken);
            await Assert.That(practiceFacts.Select(fact => fact.Result))
                .IsEquivalentTo([
                    (GameplayFactResult?)GameplayFactResult.Wrong,
                    GameplayFactResult.Correct
                ]);
            var progress = await new GetFlagAttemptState(
                    new FlagAttemptStateReader(db))
                .ExecuteAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.TeamId,
                    GameMode.Ctf,
                    CompetitionStatus.Finished,
                    cancellationToken);
            await Assert.That(progress)
                .IsEqualTo(new FlagAttemptState(null, 2, null, true));
            await Assert.That(outbox.Published.OfType<BloodAwarded>()).IsEmpty();
            db.GameplayFacts.Add(new ManualAdjustmentGameplayFact
            {
                Id = Guid.CreateVersion7(fixture.Now.AddSeconds(2)),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.TeamId,
                ActorUserId = fixture.OwnerId,
                Value = "7",
                OccurredAt = fixture.Now.AddSeconds(2),
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Applied,
                UpdatedAt = fixture.Now.AddSeconds(2)
            });
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services.BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());
            var board = await cache.CreateScoreboardAsync(
                fixture.CompetitionId,
                fixture.Now.AddMinutes(1),
                cancellationToken);
            await Assert.That(board).IsNotNull();
            await Assert.That(board!.Snapshot.Teams).HasSingleItem();
            await Assert.That(board.Snapshot.Teams[0].TeamId).IsEqualTo(fixture.TeamId);
            await Assert.That(board.Snapshot.Teams[0].Slots.Sum(slot =>
                slot.Entries.Count(entry => entry.Kind == ScoreboardEntryKind.Solve))).IsEqualTo(1);
            await Assert.That(board.Snapshot.Teams[0].TotalScore).IsEqualTo(17);
            await Assert.That(board.Snapshot.CurrentChallengeScores.Single().Score).IsEqualTo(10);
            var detail = await new ScoreboardDetailReader(db).ReadSlotAsync(new(
                fixture.CompetitionId,
                fixture.TeamId,
                fixture.CompetitionChallengeId,
                GameMode.Ctf,
                null,
                null,
                null,
                fixture.Now.AddMinutes(1),
                null,
                null,
                10), cancellationToken);
            await Assert.That(detail).HasSingleItem();
            await Assert.That(detail[0].OccurredAt < fixture.FinishedAt).IsTrue();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Finished_competition_blocks_practice_membership_changes(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_practice_teams");
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, earlyFinish: true, cancellationToken);
            var joinerId = Guid.CreateVersion7(fixture.Now.AddTicks(20));
            await using var db = new NoCtfDbContext(options);
            db.Users.Add(User(joinerId, "practice-joiner", fixture.Now));
            (await db.Competitions.AsSplitQuery().SingleAsync(cancellationToken))
                .MaxTeamMembers = 3;
            await db.SaveChangesAsync(cancellationToken);
            var outbox = new RecordingOutbox();
            var events = new CompetitionEventStore(db, outbox);
            var registration = new TeamRegistrationStore(db, outbox, eventRecorder: events);
            var created = await new CreateTeam(registration).ExecuteAsync(new(
                fixture.CompetitionId,
                fixture.OwnerId,
                "Late team",
                fixture.Now,
                CompetitionTrackConfiguration.DefaultTrackKey), cancellationToken);
            await Assert.That(created.Succeeded).IsTrue();
            await Assert.That(created.Value!.RegistrationStatus)
                .IsEqualTo(TeamRegistrationStatus.Approved);

            var formal = await db.Teams.SingleAsync(
                team => team.Id == fixture.TeamId,
                cancellationToken);
            var membership = new TeamMembershipStore(db, outbox, eventRecorder: events);
            await Assert.That(await membership.JoinByInvitationAsync(
                    fixture.CompetitionId,
                    formal.InvitationToken,
                    joinerId,
                    fixture.Now,
                    cancellationToken))
                .IsEqualTo(TeamMembershipFailure.MembershipLocked);
            await Assert.That(await membership.JoinByInvitationAsync(
                    fixture.CompetitionId,
                    (await db.Teams.SingleAsync(
                        team => team.Id == created.Value.Id,
                        cancellationToken)).InvitationToken,
                    joinerId,
                    fixture.Now,
                    cancellationToken))
                .IsEqualTo(TeamMembershipFailure.MembershipLocked);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services.BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());
            var board = await cache.CreateScoreboardAsync(
                fixture.CompetitionId,
                fixture.Now,
                cancellationToken);
            await Assert.That(board!.Snapshot.Teams.Select(entry => entry.TeamId))
                .IsEquivalentTo([fixture.TeamId]);
            await Assert.That(board.Snapshot.CurrentChallengeScores.Single().Score).IsEqualTo(10);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Runtime_backed_practice_requires_a_running_unexpired_practice_runtime(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_practice_runtime");
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, earlyFinish: false, cancellationToken);
            await using var db = new NoCtfDbContext(options);
            var template = await db.Challenges.AsSplitQuery()
                .SingleAsync(cancellationToken);
            template.Definition = TestConfigurations.Definition(
                GameMode.Ctf,
                JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    null,
                    null,
                    Runtime: new(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition(
                            "registry.example/practice:v1",
                            Security: new(false, false, false, ["ALL"], []),
                            FlagEnvironmentVariableName: "FLAG"),
                        new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                        FlagSource: RuntimeFlagSource.PerTeam)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await db.SaveChangesAsync(cancellationToken);
            var outbox = new RecordingOutbox();
            var events = new CompetitionEventStore(db, outbox);
            var clock = new FakeTimeProvider(fixture.Now);
            var intake = Intake(db, outbox, events, clock);
            var submit = new SubmitFlag(intake, new GameModeGameplayFactAdmissionPolicy());
            var command = new FlagGameplayFactCommand(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                fixture.Flag,
                fixture.Now);

            var missing = await submit.ExecuteAsync(command, cancellationToken);
            await Assert.That(missing.FailureCode)
                .IsEqualTo(GameplayFactAdmissionFailureCode.RuntimeNotRunning);

            db.RuntimeInstances.Add(new PracticeRuntimeInstance
            {
                Id = Guid.CreateVersion7(fixture.Now),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.TeamId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Running,
                CreatedAt = fixture.Now.AddMinutes(-1),
                RunningAt = fixture.Now.AddMinutes(-1),
                ExpiresAt = fixture.Now.AddMinutes(30)
            });
            await db.SaveChangesAsync(cancellationToken);

            var accepted = await submit.ExecuteAsync(command, cancellationToken);
            await Assert.That(accepted.Succeeded).IsTrue();
            await Assert.That(await db.GameplayFacts.CountAsync(cancellationToken))
                .IsEqualTo(2);
        });
    }

    private static GameplayFactIntakeStore Intake(
        NoCtfDbContext db,
        RecordingOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider clock) =>
        new(
            db,
            outbox,
            new GameplayFactAttemptCriticalSection(),
            events,
            runtimeTemplates: new ChallengeRuntimeTemplateCatalog(),
            clock: clock);

    private static async Task ProcessAsync(
        NoCtfDbContext db,
        RecordingOutbox outbox,
        ICompetitionEventRecorder events,
        Guid gameplayFactId,
        CancellationToken cancellationToken)
    {
        var processor = new GameplayFactProcessor(
            db,
            new GameModeGameplayFactEvaluatorCatalog(),
            new GameModeGameplayFactAdmissionPolicy(),
            outbox,
            Substitute.For<ILeaderboardSnapshotFactory>(),
            events);
        await processor.ProcessAsync(gameplayFactId, cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        bool earlyFinish,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var finishedAt = now.AddHours(-1);
        var startAt = now.AddHours(-3);
        var scheduledEndAt = earlyFinish ? now.AddHours(1) : finishedAt;
        var ownerId = Guid.CreateVersion7(now.AddTicks(1));
        var userId = Guid.CreateVersion7(now.AddTicks(2));
        var competitionId = Guid.CreateVersion7(now.AddTicks(3));
        var challengeId = Guid.CreateVersion7(now.AddTicks(4));
        var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(5));
        var teamId = Guid.CreateVersion7(now.AddTicks(6));
        const string flag = "flag{practice-is-a-fact}";
        db.Users.AddRange(
            User(ownerId, "practice-owner", now),
            User(userId, "practice-player", now));
        db.Competitions.Add(new CtfCompetition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Finished practice competition",
            Status = CompetitionStatus.Finished,
            PracticeModeEnabled = true,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            ModeConfiguration = TestConfigurations.Competition(
                GameMode.Ctf,
                JsonSerializer.Serialize(
                new CtfConfiguration(
                    new ScoreCurveConfiguration(
                        500,
                        0,
                        10,
                        ScoreDecayMode.Custom,
                        "eligibleTeamCount * 10m"),
                    []),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            FlagDerivationSecret = new byte[32],
            StartAt = startAt,
            EndAt = scheduledEndAt,
            CreatedAt = startAt.AddHours(-1),
            UpdatedAt = now
        });
        if (earlyFinish)
        {
            db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent
            {
                Id = Guid.CreateVersion7(finishedAt),
                CompetitionId = competitionId,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Public,
                SubjectType = EntityReferenceKind.Competition,
                SubjectId = competitionId,
                PreviousCompetitionStatus = CompetitionStatus.Running,
                CompetitionStatus = CompetitionStatus.Finished,
                Automatic = false,
                Reason = "test",
                OccurredAt = finishedAt
            });
        }
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Visibility = ChallengeVisibility.Private,
            Title = "Practice challenge",
            Direction = "Web",
            Definition = new GameModeChallengeConfigurationCatalog()
                .CreateDefaultDefinitionForTest(GameMode.Ctf),
            CreatedAt = startAt,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            Rules = TestConfigurations.Rules(
                GameMode.Ctf,
                JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    null,
                    null,
                    MaxFlagAttempts: 1),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            UpdatedAt = now
        });
        db.Teams.Add(Team(
            teamId,
            competitionId,
            userId,
            "Formal team",
            startAt.AddMinutes(-30)));
        db.ChallengeFlags.Add(new CompetitionChallengeFlag
        {
            Id = Guid.CreateVersion7(startAt),
            CompetitionChallengeId = competitionChallengeId,
            Flag = flag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            CreatedAt = startAt
        });
        db.GameplayFacts.Add(new FlagAttemptGameplayFact
        {
            Id = Guid.CreateVersion7(finishedAt.AddMinutes(-30)),
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = userId,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = finishedAt.AddMinutes(-30),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            UpdatedAt = finishedAt.AddMinutes(-30)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            finishedAt,
            ownerId,
            userId,
            competitionId,
            challengeId,
            competitionChallengeId,
            teamId,
            flag);
    }

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Throw(
                RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        DateTimeOffset now,
        DateTimeOffset endAt) => new CtfCompetition
    {
        Id = id,
        OwnerId = ownerId,
        Title = "Existing competition",
        Status = CompetitionStatus.Finished,
        ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
        FlagDerivationSecret = new byte[32],
        StartAt = endAt.AddHours(-1),
        EndAt = endAt,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid captainId,
        string name,
        DateTimeOffset registeredAt) => new()
    {
        Id = id,
        CompetitionId = competitionId,
        Name = name,
        CaptainId = captainId,
        MemberIds = [captainId],
        InvitationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
        RegistrationStatus = TeamRegistrationStatus.Approved,
        RegisteredAt = registeredAt
    };

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        DateTimeOffset Now,
        DateTimeOffset FinishedAt,
        Guid OwnerId,
        Guid UserId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        string Flag);

    private sealed class RecordingOutbox : IPostCommitMessagePublisher
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
