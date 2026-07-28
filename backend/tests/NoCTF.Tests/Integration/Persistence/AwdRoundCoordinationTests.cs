using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdRoundCoordinationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Current_round_creates_one_flag_fact_and_one_durable_successor(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awd_round")
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
            var clock = new MutableTimeProvider(fixture.Now);

            await using var db = new NoCtfDbContext(options);
            var coordinator = new PostgresAwdRoundCoordinator(
                db,
                new AwdRoundConfigurationCatalog(),
                outbox,
                clock);
            var advance = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.Now,
                    0,
                    0),
                cancellationToken);
            await Assert.That(advance).IsEqualTo(MessageExecutionOutcome.Applied);
            var generate = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await Assert.That(generate.Round).IsEqualTo(AwdRoundSpecificationId.FromRound(1));

            var generated = await coordinator.GenerateFlagsAsync(generate, cancellationToken);
            var replayed = await coordinator.GenerateFlagsAsync(generate, cancellationToken);

            await Assert.That(generated).IsEqualTo(MessageExecutionOutcome.Applied);
            await Assert.That(replayed).IsEqualTo(MessageExecutionOutcome.Idempotent);
            await Assert.That(await db.ChallengeFlags.CountAsync(cancellationToken)).IsEqualTo(1);
            var flag = await db.ChallengeFlags.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(flag.TeamId).IsEqualTo(fixture.TeamId);
            await Assert.That(flag.SpecificationId).IsEqualTo(generate.Round.Value);
            var injection = outbox.RunnerNodeMessages.OfType<InjectAwdFlag>().Single();
            await Assert.That(injection.RuntimeInstanceId).IsEqualTo(fixture.RuntimeId);
            await Assert.That(injection.ChallengeFlagId).IsEqualTo(flag.Id);
            await Assert.That(outbox.Scheduled.Select(item => item.Message)
                .OfType<AdvanceAwdRound>().Count()).IsEqualTo(1);

            var competition = await db.Competitions.SingleAsync(
                candidate => candidate.Id == fixture.CompetitionId,
                cancellationToken);
            competition.ConfigurationRevision = 1;
            await db.SaveChangesAsync(cancellationToken);
            var configurationReplacement = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.Now,
                    1,
                    0),
                cancellationToken);
            await Assert.That(configurationReplacement)
                .IsEqualTo(MessageExecutionOutcome.DeferredSchedule);

            var extendedUntil = generate.ValidUntil.AddMinutes(2);
            await db.ChallengeFlags.ExecuteUpdateAsync(
                setters => setters.SetProperty(flagFact => flagFact.ValidUntil, extendedUntil),
                cancellationToken);
            var resumeReplacement = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.Now,
                    1,
                    0),
                cancellationToken);
            await Assert.That(resumeReplacement).IsEqualTo(MessageExecutionOutcome.DeferredSchedule);
            await Assert.That(outbox.Scheduled.Select(item => item.Message)
                .OfType<AdvanceAwdRound>().Count()).IsEqualTo(3);
            var currentRoundGenerate = generate with
            {
                ValidUntil = extendedUntil,
                CompetitionConfigurationRevision = 1
            };

            var greenTeamId = Guid.CreateVersion7();
            var greenRuntimeId = Guid.CreateVersion7();
            db.Teams.Add(new Team
            {
                Id = greenTeamId,
                CompetitionId = fixture.CompetitionId,
                Name = "Green",
                NormalizedName = "GREEN",
                CaptainId = fixture.OwnerId,
                MemberIds = [fixture.OwnerId],
                InvitationToken = "fedcba9876543210fedcba9876543210",
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = fixture.Now
            });
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = greenRuntimeId,
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = greenTeamId,
                Generation = 1,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerPool = "default",
                RunnerId = "runner-a",
                State = RuntimeState.Provisioning,
                CreatedAt = fixture.Now
            });
            await db.SaveChangesAsync(cancellationToken);
            var generatedForLateTeam = await coordinator.GenerateFlagsAsync(
                currentRoundGenerate,
                cancellationToken);
            await Assert.That(generatedForLateTeam).IsEqualTo(MessageExecutionOutcome.Applied);
            await Assert.That(outbox.Scheduled.Select(item => item.Message)
                .OfType<AdvanceAwdRound>().Count()).IsEqualTo(3);
            await RuntimeWriteBackHandler.Handle(
                new RuntimeProvisioned(
                    greenRuntimeId,
                    0,
                    "runner-a",
                    RuntimeProvider.Docker,
                    JsonSerializer.Serialize(new ContainerReceipt(
                        greenRuntimeId,
                        RuntimeProvider.Docker,
                        "green-container",
                        RuntimeStatus.Running,
                        new Dictionary<int, int>(),
                        null,
                        null)),
                    [],
                    [],
                    null,
                    AwdCheckerTargetUrl: "http://green-container:8080/health"),
                db,
                outbox,
                cancellationToken);
            var runningGreenRuntime = await db.RuntimeInstances.SingleAsync(
                runtime => runtime.Id == greenRuntimeId,
                cancellationToken);
            await Assert.That(runningGreenRuntime.AwdCheckerTargetUrl)
                .IsEqualTo("http://green-container:8080/health");
            await Assert.That(runningGreenRuntime.NextCheckerDueAt).IsNotNull();
            var deferredInjection = outbox.RunnerNodeMessages.OfType<InjectAwdFlag>().Last();
            var greenFlag = await db.ChallengeFlags.AsNoTracking().SingleAsync(
                candidate => candidate.TeamId == greenTeamId
                    && candidate.SpecificationId == currentRoundGenerate.Round.Value,
                cancellationToken);
            await Assert.That(deferredInjection.ChallengeFlagId).IsEqualTo(greenFlag.Id);

            clock.UtcNow = fixture.Now.AddMinutes(17);
            var delayedAdvance = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    currentRoundGenerate.ValidUntil,
                    1,
                    0),
                cancellationToken);
            await Assert.That(delayedAdvance).IsEqualTo(MessageExecutionOutcome.Applied);
            var currentGenerate = outbox.Published.OfType<GenerateAwdFlags>().Last();
            await Assert.That(currentGenerate.Round).IsEqualTo(AwdRoundSpecificationId.FromRound(2));
            await Assert.That(currentGenerate.ValidStart).IsLessThanOrEqualTo(clock.UtcNow);
            await Assert.That(currentGenerate.ValidUntil).IsGreaterThan(clock.UtcNow);
            var currentGenerated = await coordinator.GenerateFlagsAsync(currentGenerate, cancellationToken);
            await Assert.That(currentGenerated).IsEqualTo(MessageExecutionOutcome.Applied);
            await Assert.That(await db.ChallengeFlags.CountAsync(cancellationToken)).IsEqualTo(4);

            var staleGenerate = await coordinator.GenerateFlagsAsync(generate, cancellationToken);
            await Assert.That(staleGenerate).IsEqualTo(MessageExecutionOutcome.Superseded);

            await db.Teams.ExecuteUpdateAsync(
                setters => setters.SetProperty(team => team.IsBanned, true),
                cancellationToken);
            clock.UtcNow = currentGenerate.ValidUntil;
            await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    currentGenerate.ValidUntil,
                    1,
                    0),
                cancellationToken);
            var emptyRound = outbox.Published.OfType<GenerateAwdFlags>().Last();
            var emptyGenerated = await coordinator.GenerateFlagsAsync(emptyRound, cancellationToken);
            await Assert.That(emptyGenerated).IsEqualTo(MessageExecutionOutcome.Applied);
            await Assert.That(outbox.Scheduled.Select(item => item.Message)
                .OfType<AdvanceAwdRound>().Count()).IsEqualTo(5);

            var stale = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.Now,
                    0,
                    0),
                cancellationToken);
            await Assert.That(stale).IsEqualTo(MessageExecutionOutcome.Superseded);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;
        var now = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond) + 1);
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
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
            Title = "AWD round",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            ConfigurationJson = JsonSerializer.Serialize(
                AwdConfiguration.Default with
                {
                    HardeningDurationSeconds = 600,
                    RoundDurationSeconds = 300
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RunningSince = now.AddMinutes(-12),
            StartAt = now.AddHours(-1),
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
            Title = "AWD service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            ConfigurationJson = JsonSerializer.Serialize(
                new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Blue",
            NormalizedName = "BLUE",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = "0123456789abcdef0123456789abcdef",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        var runtimeId = Guid.CreateVersion7();
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "default",
            RunnerId = "runner-a",
            State = RuntimeState.Running,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, ownerId, competitionId, competitionChallengeId, teamId, runtimeId);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid RuntimeId);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerNodeMessages { get; } = [];
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

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            RunnerNodeMessages.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
