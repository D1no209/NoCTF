using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionLifecycle")]
public sealed class CompetitionLifecyclePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Start_gate_rejects_persisted_base_score_and_hint_cost_above_the_limit(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_score_start_gate")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            var competitionId = Guid.CreateVersion7(now);
            var challengeId = Guid.CreateVersion7(now);
            var competitionChallengeId = Guid.CreateVersion7(now);
            var teamId = Guid.CreateVersion7(now);
            var configurations = new GameModeChallengeConfigurationCatalog();

            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.MigrateAsync(cancellationToken);
                seed.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "score-owner",
                    NormalizedUserName = "SCORE-OWNER",
                    Email = "score-owner@example.test",
                    NormalizedEmail = "SCORE-OWNER@EXAMPLE.TEST",
                    PasswordHash = "test",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    Title = "Score start gate",
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Published,
                    ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
                    StartAt = now.AddMinutes(-1),
                    EndAt = now.AddHours(1),
                    FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConfigurationUpdatedAt = now
                });
                seed.Challenges.Add(new Challenge
                {
                    Id = challengeId,
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Title = "Persisted score limit",
                    DefinitionJson = configurations.GetDefaultDefinitionJson(GameMode.Ctf),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    BaseScore = ScoreValueLimits.MaximumConfiguredValue + 1,
                    IsPublished = true,
                    RulesJson = configurations.GetDefaultJson(GameMode.Ctf),
                    Hints =
                    [
                        new CompetitionChallengeHint
                        {
                            Id = Guid.CreateVersion7(now),
                            Content = "Persisted over-limit hint",
                            Cost = ScoreValueLimits.MaximumConfiguredValue + 1
                        }
                    ],
                    UpdatedAt = now
                });
                seed.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = competitionId,
                    Name = "Score Team",
                    NormalizedName = "SCORE TEAM",
                    CaptainId = ownerId,
                    MemberIds = [ownerId],
                    InvitationToken = new string('s', 32),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var db = new NoCtfDbContext(options);
            var errors = await new CompetitionStartGate(
                    new CompetitionStartGateStore(db),
                    new GameModeCompetitionConfigurationValidator(),
                    configurations)
                .ValidateAsync(competitionId, cancellationToken);

            await Assert.That(errors).IsNotNull();
            await Assert.That(errors!.Any(error =>
                    error.Code == StartGateFailureCode.ChallengeRulesInvalid
                    && error.Message.Contains("BaseScore", StringComparison.Ordinal)))
                .IsTrue();
            await Assert.That(errors.Any(error =>
                    error.Code == StartGateFailureCode.ChallengeRulesInvalid
                    && error.Message.Contains("Hint cost", StringComparison.Ordinal)))
                .IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awd_start_rejects_a_published_challenge_without_a_runtime(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awd_start_gate")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedIncompleteAwdAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            var configurations = new GameModeChallengeConfigurationCatalog();

            await using var db = new NoCtfDbContext(options);
            var gate = new CompetitionStartGate(
                new CompetitionStartGateStore(db),
                new GameModeCompetitionConfigurationValidator(),
                configurations);
            var errors = await gate.ValidateAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(errors).IsNotNull();
            var runtimeError = errors!.Single(error =>
                error.Code == StartGateFailureCode.RuntimeDefinitionInvalid &&
                error.CompetitionChallengeId == fixture.CompetitionChallengeId);
            await Assert.That(runtimeError.Message)
                .IsEqualTo("Runtime is required before an AWD competition can start.");

            var store = new CompetitionLifecycleStore(
                db,
                gate,
                outbox,
                new CompetitionEventStore(db, outbox));
            var transitioned = await store.TryTransitionWithAuditAsync(
                fixture.CompetitionId,
                CompetitionStatus.Published,
                CompetitionStatus.Running,
                fixture.OwnerId,
                "manual_start",
                false,
                CompetitionLifecycleEffects.ProvisionRuntimes,
                cancellationToken);

            await Assert.That(transitioned).IsFalse();
            db.ChangeTracker.Clear();
            await Assert.That(await db.Competitions.AsNoTracking()
                    .Where(competition => competition.Id == fixture.CompetitionId)
                    .Select(competition => competition.Status)
                    .SingleAsync(cancellationToken))
                .IsEqualTo(CompetitionStatus.Published);
            await Assert.That(await db.CompetitionEvents.AsNoTracking()
                    .AnyAsync(@event => @event.CompetitionId == fixture.CompetitionId
                        && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged,
                        cancellationToken))
                .IsFalse();
            await Assert.That(await db.RuntimeInstances.AsNoTracking()
                    .AnyAsync(runtime => runtime.CompetitionId == fixture.CompetitionId, cancellationToken))
                .IsFalse();
            await Assert.That(outbox.Published).IsEmpty();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awd_pause_and_resume_freeze_checkers_and_extend_the_current_flag_window(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_lifecycle")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            var pausedAt = fixture.Now.AddMinutes(-5);

            await using (var pauseDb = new NoCtfDbContext(options))
            {
                var eventStore = new CompetitionEventStore(pauseDb, outbox);
                var store = new CompetitionLifecycleStore(
                    pauseDb,
                    null!,
                    outbox,
                    new PauseEventTimeRecorder(eventStore, pausedAt));
                var applied = await store.TryTransitionWithAuditAsync(
                    fixture.CompetitionId,
                    CompetitionStatus.Running,
                    CompetitionStatus.Paused,
                    fixture.OwnerId,
                    "pause",
                    false,
                    CompetitionLifecycleEffects.None,
                    cancellationToken);
                await Assert.That(applied).IsTrue();
            }

            await using (var resumeDb = new NoCtfDbContext(options))
            {
                var store = new CompetitionLifecycleStore(
                    resumeDb,
                    null!,
                    outbox,
                    new CompetitionEventStore(resumeDb, outbox));
                var applied = await store.TryTransitionWithAuditAsync(
                    fixture.CompetitionId,
                    CompetitionStatus.Paused,
                    CompetitionStatus.Running,
                    fixture.OwnerId,
                    "resume",
                    false,
                    CompetitionLifecycleEffects.ProvisionRuntimes,
                    cancellationToken);
                await Assert.That(applied).IsTrue();
            }

            await using var verify = new NoCtfDbContext(options);
            var competition = await verify.Competitions.AsNoTracking().SingleAsync(
                item => item.Id == fixture.CompetitionId,
                cancellationToken);
            await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Running);
            await Assert.That(competition.RunningSince).IsNotNull();
            await Assert.That(competition.AccumulatedRunningSeconds).IsGreaterThan(0);
            var runtime = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                item => item.Id == fixture.RuntimeId,
                cancellationToken);
            await Assert.That(runtime.NextCheckerDueAt).IsNotNull();
            await Assert.That(runtime.NextCheckerDueAt!.Value)
                .IsGreaterThanOrEqualTo(pausedAt.AddMinutes(5));
            await Assert.That(runtime.CheckerSequence).IsEqualTo(1);
            var flag = await verify.ChallengeFlags.AsNoTracking().SingleAsync(
                item => item.Id == fixture.FlagId,
                cancellationToken);
            await Assert.That(flag.ValidUntil).IsNotNull();
            await Assert.That(flag.ValidUntil!.Value)
                .IsGreaterThan(fixture.OriginalValidUntil.AddMinutes(4));
            await Assert.That(await verify.CompetitionEvents.AsNoTracking()
                    .CountAsync(@event => @event.CompetitionId == fixture.CompetitionId
                        && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged,
                        cancellationToken))
                .IsEqualTo(2);
            await Assert.That((await verify.Competitions.AsNoTracking().SingleAsync(
                competition => competition.Id == fixture.CompetitionId, cancellationToken)).LeaderboardDirty).IsTrue();
            await Assert.That(outbox.Published.OfType<ProvisionCompetitionRuntimes>().Count())
                .IsEqualTo(1);
        });
    }

    private static async Task<IncompleteAwdFixture> SeedIncompleteAwdAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now);
        var challengeId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now);
        var teamId = Guid.CreateVersion7(now);
        var configurations = new GameModeChallengeConfigurationCatalog();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "awd-owner",
            NormalizedUserName = "AWD-OWNER",
            Email = "awd-owner@example.test",
            NormalizedEmail = "AWD-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Incomplete AWD",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Published,
            ConfigurationJson = JsonSerializer.Serialize(
                AwdConfiguration.Default,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Title = "Missing runtime",
            DefinitionJson = configurations.GetDefaultDefinitionJson(GameMode.Awd),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = configurations.GetDefaultJson(GameMode.Awd),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "AWD Team",
            NormalizedName = "AWD TEAM",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(ownerId, competitionId, competitionChallengeId);
    }

    private static async Task<LifecycleFixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        var flagId = Guid.CreateVersion7();
        var originalValidUntil = now.AddMinutes(5);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWD lifecycle",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            RunningSince = now.AddMinutes(-2),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "awd",
            State = RuntimeState.Running,
            ProcessingVersion = 1,
            ProviderReceiptJson = "{\"id\":\"runtime\"}",
            NextCheckerDueAt = now.AddMinutes(1),
            CreatedAt = now,
            RunningAt = now
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = flagId,
            CompetitionChallengeId = competitionChallengeId,
            Flag = "flag{pause}",
            FlagSha256 = new byte[32],
            SpecificationKind = SpecificationKind.AwdRound,
            SpecificationId = AwdRoundSpecificationId.FromRound(1).Value,
            ValidStart = now.AddMinutes(-10),
            ValidUntil = originalValidUntil,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            ownerId,
            competitionId,
            runtimeId,
            flagId,
            originalValidUntil);
    }

    private sealed record LifecycleFixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid CompetitionId,
        Guid RuntimeId,
        Guid FlagId,
        DateTimeOffset OriginalValidUntil);

    private sealed record IncompleteAwdFixture(
        Guid OwnerId,
        Guid CompetitionId,
        Guid CompetitionChallengeId);

    private sealed class PauseEventTimeRecorder(
        ICompetitionEventRecorder inner,
        DateTimeOffset pausedAt) : ICompetitionEventRecorder
    {
        public ValueTask<Guid> RecordAsync(
            CompetitionEventDraft draft,
            CancellationToken cancellationToken = default) =>
            inner.RecordAsync(
                draft.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                    && draft.CompetitionStatus == CompetitionStatus.Paused
                    ? draft with { OccurredAt = pausedAt }
                    : draft,
                cancellationToken);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];

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

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

}
