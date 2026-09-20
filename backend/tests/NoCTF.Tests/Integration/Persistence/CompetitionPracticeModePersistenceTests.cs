using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task Generated_marker_removal_migration_preserves_existing_teams(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_practice_migration");
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres);
            await using var db = new NoCtfDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync(
                "20260910145118_UserWallpaperPreferences",
                cancellationToken);
            // The current EF model includes columns added after this historical target.
            // Add the current column only for seeding, then remove it so the generated
            // migration under test still owns its schema transition.
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE competitions ADD COLUMN tracks_enabled boolean NOT NULL DEFAULT TRUE",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE competitions ADD COLUMN access_mode smallint NOT NULL DEFAULT 0",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE competitions ADD COLUMN write_up_submission_required boolean NOT NULL DEFAULT FALSE, ADD COLUMN write_up_submission_deadline_hours integer NOT NULL DEFAULT 0",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE teams ADD COLUMN write_up_file_id uuid NULL, ADD COLUMN write_up_submitted_at timestamp with time zone NULL, ADD COLUMN write_up_submitted_by_user_id uuid NULL",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE users ADD COLUMN external_identity_bound_at timestamp with time zone NULL, ADD COLUMN external_identity_namespace varchar(512) NULL, ADD COLUMN external_identity_protocol smallint NULL, ADD COLUMN external_identity_provider_id uuid NULL, ADD COLUMN external_identity_subject varchar(255) NULL",
                cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var user = User(Guid.CreateVersion7(now), "migration-team-owner", now);
            var competition = Competition(
                Guid.CreateVersion7(now.AddTicks(1)),
                user.Id,
                now,
                now.AddHours(-1));
            db.Users.Add(user);
            db.Competitions.Add(competition);
            db.Teams.Add(Team(
                Guid.CreateVersion7(now.AddTicks(2)),
                competition.Id,
                user.Id,
                "Existing team",
                now.AddHours(-2)));
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE competitions DROP COLUMN tracks_enabled",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE competitions DROP COLUMN access_mode",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE competitions DROP COLUMN write_up_submission_required, DROP COLUMN write_up_submission_deadline_hours",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE teams DROP COLUMN write_up_file_id, DROP COLUMN write_up_submitted_at, DROP COLUMN write_up_submitted_by_user_id",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE users DROP COLUMN external_identity_bound_at, DROP COLUMN external_identity_namespace, DROP COLUMN external_identity_protocol, DROP COLUMN external_identity_provider_id, DROP COLUMN external_identity_subject",
                cancellationToken);
            db.ChangeTracker.Clear();

            await db.Database.MigrateAsync(cancellationToken);

            await Assert.That(await db.Teams.AsNoTracking().CountAsync(cancellationToken))
                .IsEqualTo(1);
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'teams'
                      AND column_name = 'is_practice_team')
                """;
            var markerExists = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            await Assert.That(markerExists).IsFalse();
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
                    intake,
                    new GameModeGameplayFactAdmissionPolicy())
                .ExecuteAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    cancellationToken);
            await Assert.That(progress)
                .IsEqualTo(new FlagAttemptState(null, 2, null, true));
            await Assert.That(outbox.Published.OfType<BloodAwarded>()).IsEmpty();
            db.GameplayFacts.Add(new GameplayFact
            {
                Id = Guid.CreateVersion7(fixture.Now.AddSeconds(2)),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.TeamId,
                ActorUserId = fixture.OwnerId,
                Kind = GameplayFactKind.ManualAdjustment,
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
            var board = await cache.CreateAsync(
                fixture.CompetitionId,
                fixture.Now.AddMinutes(1),
                cancellationToken);
            await Assert.That(board).IsNotNull();
            await Assert.That(board!.Entries).HasSingleItem();
            await Assert.That(board.Entries[0].TeamId).IsEqualTo(fixture.TeamId);
            await Assert.That(board.Entries[0].SolveCount).IsEqualTo(1);
            await Assert.That(board.Entries[0].Score).IsEqualTo(17);
            await Assert.That(board.Challenges.Single().CurrentScore).IsEqualTo(10);
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
    public async Task Only_teams_registered_after_the_official_cutoff_accept_practice_membership(
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
            (await db.Competitions.SingleAsync(cancellationToken)).MaxTeamMembers = 3;
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
                .IsNull();

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services.BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());
            var board = await cache.CreateAsync(
                fixture.CompetitionId,
                fixture.Now,
                cancellationToken);
            await Assert.That(board!.Entries.Select(entry => entry.TeamId))
                .IsEquivalentTo([fixture.TeamId]);
            await Assert.That(board.Challenges.Single().CurrentScore).IsEqualTo(10);
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
            var template = await db.Challenges.SingleAsync(cancellationToken);
            template.DefinitionJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    Runtime: new(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition(
                            "registry.example/practice:v1",
                            FlagEnvironmentVariableName: "FLAG"),
                        new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                        FlagSource: RuntimeFlagSource.PerTeam)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = Guid.CreateVersion7(fixture.Now),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.TeamId,
                Purpose = RuntimePurpose.Practice,
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
            new GameplayFactAttemptCriticalSection(
                new AsyncKeyedLock.AsyncKeyedLocker<string>()),
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
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Finished practice competition",
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Finished,
            PracticeModeEnabled = true,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            ConfigurationJson = JsonSerializer.Serialize(
                new CtfConfiguration(
                    CtfConfiguration.CurrentSchemaVersion,
                    new ScoreCurveConfiguration(
                        500,
                        0,
                        10,
                        ScoreDecayMode.Custom,
                        "eligibleTeamCount * 10m"),
                    []),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            FlagDerivationSecret = new byte[32],
            StartAt = startAt,
            EndAt = scheduledEndAt,
            CreatedAt = startAt.AddHours(-1),
            UpdatedAt = now
        });
        if (earlyFinish)
        {
            db.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = Guid.CreateVersion7(finishedAt),
                CompetitionId = competitionId,
                Kind = CompetitionEventKind.CompetitionLifecycleChanged,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Public,
                SubjectType = EntityReferenceKind.Competition,
                SubjectId = competitionId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    competitionStatus = CompetitionStatus.Finished,
                    from = CompetitionStatus.Running,
                    to = CompetitionStatus.Finished,
                    automatic = false,
                    reason = "test"
                }),
                OccurredAt = finishedAt
            });
        }
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = "Practice challenge",
            Direction = "Web",
            DefinitionJson = new GameModeChallengeConfigurationCatalog()
                .GetDefaultDefinitionJson(GameMode.Ctf),
            CreatedAt = startAt,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    MaxFlagAttempts: 1),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        });
        db.Teams.Add(Team(
            teamId,
            competitionId,
            userId,
            "Formal team",
            startAt.AddMinutes(-30)));
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(startAt),
            CompetitionChallengeId = competitionChallengeId,
            Flag = flag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            CreatedAt = startAt
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = Guid.CreateVersion7(finishedAt.AddMinutes(-30)),
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = userId,
            Kind = GameplayFactKind.FlagAttempt,
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
            .Options;

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        DateTimeOffset now,
        DateTimeOffset endAt) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = "Existing competition",
        Mode = GameMode.Ctf,
        Status = CompetitionStatus.Finished,
        ConfigurationJson = "{}",
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

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
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
