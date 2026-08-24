using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Worker;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdRoundCoordinationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Rebuilt_schedule_resumes_at_current_round_without_historical_ticks(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_schedule_rebuild")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var resumedAt = fixture.Now.AddMinutes(17);

            await using var db = new NoCtfDbContext(options);
            var source = new PostgresClusterScheduleSource(
                db,
                new AwdRoundConfigurationCatalog(),
                new KohProducerConfigurationCatalog());
            var rebuilt = await source.RebuildAsync(resumedAt, cancellationToken);

            var entry = rebuilt.Single();
            await Assert.That(entry.Kind).IsEqualTo(ClusterScheduleKind.AwdRound);
            await Assert.That(entry.DueAt).IsEqualTo(resumedAt);
            var message = (AdvanceAwdRound)entry.Message;
            var outbox = new RecordingOutbox();
            var coordinator = new PostgresAwdRoundCoordinator(
                db,
                new AwdRoundConfigurationCatalog(),
                outbox,
                new MutableTimeProvider(resumedAt));

            var outcome = await coordinator.AdvanceAsync(message, cancellationToken);

            await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            var generate = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await Assert.That(generate.Round).IsEqualTo(AwdRoundSpecificationId.FromRound(4));
            await Assert.That(generate.ValidStart).IsLessThan(resumedAt);
            await Assert.That(generate.ValidUntil).IsGreaterThan(resumedAt);
            await Assert.That(outbox.Scheduled).IsEmpty();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Checker_executions_create_independent_facts_and_callback_retries_are_idempotent(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_checker_facts")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken, includeChecker: true);
            var outbox = new RecordingOutbox();

            await using (var dispatchDb = new NoCtfDbContext(options))
            {
                var first = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new DispatchAwdCheckers(fixture.Now),
                    dispatchDb,
                    new AwdCheckerConfigurationCatalog(),
                    outbox,
                    cancellationToken);
                var replay = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new DispatchAwdCheckers(fixture.Now),
                    dispatchDb,
                    new AwdCheckerConfigurationCatalog(),
                    outbox,
                    cancellationToken);

                await Assert.That(first).IsEqualTo(MessageExecutionOutcome.Applied);
                await Assert.That(replay).IsEqualTo(MessageExecutionOutcome.Idempotent);
            }

            Guid healthyFactId;
            await using (var verification = new NoCtfDbContext(options))
            {
                var facts = await verification.GameplayFacts.AsNoTracking()
                    .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition)
                    .ToListAsync(cancellationToken);
                await Assert.That(facts.Count).IsEqualTo(1);
                await Assert.That(facts[0].State).IsEqualTo(GameplayFactState.Processing);
                healthyFactId = facts[0].Id;
            }

            await using (var healthyDb = new NoCtfDbContext(options))
            {
                var store = new InternalResultStore(healthyDb, new RecordingOutbox());
                var applied = await store.RecordAwdAsync(AwdCheckResult.Create(
                    fixture.RuntimeId,
                    healthyFactId,
                    AwdServiceState.Up,
                    fixture.Now), cancellationToken);
                var replay = await store.RecordAwdAsync(AwdCheckResult.Create(
                    fixture.RuntimeId,
                    healthyFactId,
                    AwdServiceState.Down,
                    fixture.Now), cancellationToken);
                await Assert.That(applied).IsEqualTo(InternalResultDisposition.Applied);
                await Assert.That(replay).IsEqualTo(InternalResultDisposition.Duplicate);
            }

            var nextCheckAt = fixture.Now.AddSeconds(
                AwdConfiguration.Default.CheckerIntervalSeconds);
            await using (var dispatchDb = new NoCtfDbContext(options))
            {
                var next = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new DispatchAwdCheckers(nextCheckAt),
                    dispatchDb,
                    new AwdCheckerConfigurationCatalog(),
                    outbox,
                    cancellationToken);
                await Assert.That(next).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            Guid unhealthyFactId;
            await using (var verification = new NoCtfDbContext(options))
            {
                var facts = await verification.GameplayFacts.AsNoTracking()
                    .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition)
                    .OrderBy(fact => fact.OccurredAt)
                    .ThenBy(fact => fact.Id)
                    .ToListAsync(cancellationToken);
                await Assert.That(facts.Count).IsEqualTo(2);
                await Assert.That(facts[0].Result).IsEqualTo(GameplayFactResult.ServiceUp);
                unhealthyFactId = facts[1].Id;
            }

            await using var firstCallbackDb = new NoCtfDbContext(options);
            await using var retryCallbackDb = new NoCtfDbContext(options);
            var callbackResults = await Task.WhenAll(
                new InternalResultStore(firstCallbackDb, new RecordingOutbox())
                    .RecordAwdAsync(AwdCheckResult.Create(
                        fixture.RuntimeId,
                        unhealthyFactId,
                        AwdServiceState.CheckerTimedOut,
                        nextCheckAt), cancellationToken),
                new InternalResultStore(retryCallbackDb, new RecordingOutbox())
                    .RecordAwdAsync(AwdCheckResult.Create(
                        fixture.RuntimeId,
                        unhealthyFactId,
                        AwdServiceState.CheckerTimedOut,
                        nextCheckAt), cancellationToken));

            await Assert.That(callbackResults.Count(result =>
                    result == InternalResultDisposition.Applied))
                .IsEqualTo(1);
            await Assert.That(callbackResults.Count(result =>
                    result == InternalResultDisposition.Duplicate))
                .IsEqualTo(1);
            await using var final = new NoCtfDbContext(options);
            var finalFacts = await final.GameplayFacts.AsNoTracking()
                .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition)
                .OrderBy(fact => fact.OccurredAt)
                .ThenBy(fact => fact.Id)
                .ToListAsync(cancellationToken);
            await Assert.That(finalFacts.Count).IsEqualTo(2);
            await Assert.That(finalFacts[1].State).IsEqualTo(GameplayFactState.Completed);
            await Assert.That(finalFacts[1].Result).IsEqualTo(GameplayFactResult.ServiceDown);
            await Assert.That(finalFacts[1].FailureCode)
                .IsEqualTo(GameplayFactFailureCode.CheckerPlatformError);
            await Assert.That(outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Count())
                .IsEqualTo(2);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Current_round_creates_one_flag_fact_without_recursive_schedule(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
                    fixture.Now),
                cancellationToken);
            await Assert.That(advance).IsEqualTo(MessageExecutionOutcome.Applied);
            var generate = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await Assert.That(generate.Round).IsEqualTo(AwdRoundSpecificationId.FromRound(1));
            await Assert.That(outbox.Published.OfType<ProjectLeaderboard>()).IsEmpty();

            var generated = await coordinator.GenerateFlagsAsync(generate, cancellationToken);
            var replayed = await coordinator.GenerateFlagsAsync(generate, cancellationToken);

            await Assert.That(generated).IsEqualTo(MessageExecutionOutcome.Applied);
            await Assert.That(replayed).IsEqualTo(MessageExecutionOutcome.Idempotent);
            await Assert.That(await db.ChallengeFlags.CountAsync(cancellationToken)).IsEqualTo(1);
            var flag = await db.ChallengeFlags.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(flag.TeamId).IsEqualTo(fixture.TeamId);
            await Assert.That(flag.SpecificationId).IsEqualTo(generate.Round.Value);
            await Assert.That(flag.Flag).StartsWith("challenge{");
            var injection = outbox.RunnerNodeMessages.OfType<InjectAwdFlag>().Single();
            await Assert.That(injection.RuntimeInstanceId).IsEqualTo(fixture.RuntimeId);
            await Assert.That(injection.ChallengeFlagId).IsEqualTo(flag.Id);
            await Assert.That(outbox.Scheduled).IsEmpty();

            var extendedUntil = generate.ValidUntil.AddMinutes(2);
            await db.ChallengeFlags.ExecuteUpdateAsync(
                setters => setters.SetProperty(flagFact => flagFact.ValidUntil, extendedUntil),
                cancellationToken);
            var resumeReplacement = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.Now),
                cancellationToken);
            await Assert.That(resumeReplacement).IsEqualTo(MessageExecutionOutcome.DeferredSchedule);
            await Assert.That(outbox.Scheduled).IsEmpty();
            var currentRoundGenerate = generate with { ValidUntil = extendedUntil };

            var greenTeamId = Guid.CreateVersion7();
            var greenRuntimeId = Guid.CreateVersion7();
            db.Teams.Add(new Team
            {
                Id = greenTeamId,
                CompetitionId = fixture.CompetitionId,
                Name = "Green",
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
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerId = "runner-a",
                State = RuntimeState.Provisioning,
                CreatedAt = fixture.Now
            });
            await db.SaveChangesAsync(cancellationToken);
            var generatedForLateTeam = await coordinator.GenerateFlagsAsync(
                currentRoundGenerate,
                cancellationToken);
            await Assert.That(generatedForLateTeam).IsEqualTo(MessageExecutionOutcome.Applied);
            await Assert.That(outbox.Scheduled).IsEmpty();
            await RuntimeWriteBackHandler.Handle(
                new RuntimeProvisioned(
                    greenRuntimeId,
                    "runner-a",
                    RuntimeProvider.Docker,
                    JsonSerializer.Serialize(new ContainerReceipt(
                        greenRuntimeId,
                        RuntimeProvider.Docker,
                        "green-container",
                        RuntimeStatus.Running,
                        new Dictionary<int, int>(),
                        null,
                        "green-container",
                        RuntimeInstanceId: greenRuntimeId)),
                    [],
                    null),
                db,
                outbox,
                cancellationToken);
            var runningGreenRuntime = await db.RuntimeInstances.SingleAsync(
                runtime => runtime.Id == greenRuntimeId,
                cancellationToken);
            await Assert.That(runningGreenRuntime.State).IsEqualTo(RuntimeState.Running);
            await Assert.That(runningGreenRuntime.ProviderReceiptJson).IsNotNull();
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
                    currentRoundGenerate.ValidUntil),
                cancellationToken);
            await Assert.That(delayedAdvance).IsEqualTo(MessageExecutionOutcome.Applied);
            var currentGenerate = outbox.Published.OfType<GenerateAwdFlags>().Last();
            await Assert.That(currentGenerate.Round).IsEqualTo(AwdRoundSpecificationId.FromRound(2));
            var roundProjection = outbox.Published.OfType<ProjectLeaderboard>().Single();
            await Assert.That(roundProjection.CompetitionId).IsEqualTo(fixture.CompetitionId);
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
                    currentGenerate.ValidUntil),
                cancellationToken);
            await Assert.That(outbox.Published.OfType<GenerateAwdFlags>().Count()).IsEqualTo(2);
            await Assert.That(outbox.Scheduled).IsEmpty();

            var stale = await coordinator.AdvanceAsync(
                new AdvanceAwdRound(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.Now),
                cancellationToken);
            await Assert.That(stale).IsEqualTo(MessageExecutionOutcome.Idempotent);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken,
        bool includeChecker = false)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
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
                    RoundDurationSeconds = 300,
                    FlagTemplate = new("competition", "[TEAMHASH]", false)
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = Guid.CreateVersion7(now.AddMinutes(-10)),
            CompetitionId = competitionId,
            Kind = CompetitionEventKind.CompetitionLifecycleChanged,
            Level = CompetitionEventLevel.Information,
            Visibility = CompetitionEventVisibility.Public,
            SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.Competition,
            SubjectId = competitionId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                from = CompetitionStatus.Published,
                to = CompetitionStatus.Running,
                automatic = false,
                reason = (string?)null
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            OccurredAt = now.AddMinutes(-10)
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Title = "AWD service",
            DefinitionJson = JsonSerializer.Serialize(
                new AwdChallengeConfiguration(
                    AwdChallengeConfiguration.CurrentSchemaVersion,
                    Checker: includeChecker
                        ? new(new("checker:test"))
                        : null),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(
                new AwdChallengeConfiguration(
                    AwdChallengeConfiguration.CurrentSchemaVersion,
                    FlagTemplate: new("challenge", "[TEAMHASH]", false)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Blue",
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
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
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
