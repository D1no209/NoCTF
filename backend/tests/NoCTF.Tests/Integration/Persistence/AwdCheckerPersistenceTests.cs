using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.Infrastructure.Submissions.Processing;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdCheckerPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Dispatcher_and_callbacks_keep_one_inflight_checker_and_only_persist_state_changes(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awd_checker")
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
            var catalog = new AwdCheckerConfigurationCatalog();

            await using (var dispatchDb = new NoCtfDbContext(options))
            {
                var outcome = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now, 1), dispatchDb, catalog, outbox, cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            var first = outbox.NodeMessages.OfType<RunAwdChecker>().Single();
            await Assert.That(first.CheckerSequence).IsEqualTo(1);
            await Assert.That(first.ProcessingVersion).IsEqualTo(7);
            await using (var fenceDb = new NoCtfDbContext(options))
            {
                var deadlineMatches = await fenceDb.RuntimeInstances.AsNoTracking()
                    .AnyAsync(runtime => runtime.Id == first.RuntimeInstanceId
                        && runtime.CheckerDeadlineAt == first.Deadline,
                        cancellationToken);
                await Assert.That(deadlineMatches).IsTrue();
            }
            await using (var inflightDb = new NoCtfDbContext(options))
            {
                var runtime = await inflightDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.NextCheckerDueAt = fixture.Now.AddSeconds(1);
                await inflightDb.SaveChangesAsync(cancellationToken);
                var schedule = await inflightDb.DurableMaintenanceSchedules.SingleAsync(
                    item => item.Kind == NoCTF.Domain.Platform.MaintenanceChainKind.AwdCheckerDispatch,
                    cancellationToken);
                var outcome = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now.AddSeconds(1), schedule.ProcessingVersion),
                    inflightDb, catalog, outbox, cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Idempotent);
                await Assert.That(outbox.NodeMessages.OfType<RunAwdChecker>().Count()).IsEqualTo(1);
            }
            var up = AwdCheckResult.Create(
                fixture.RuntimeId, 3, 1, 7, AwdServiceState.Up, fixture.Now.AddSeconds(1));
            var concurrentCallbacks = await Task.WhenAll(
                RecordAsync(options, outbox, up, cancellationToken),
                RecordAsync(options, outbox, up, cancellationToken));
            await Assert.That(concurrentCallbacks.Count(
                outcome => outcome == InternalResultDisposition.Applied)).IsEqualTo(1);
            await Assert.That(concurrentCallbacks.Count(
                outcome => outcome == InternalResultDisposition.Duplicate)).IsEqualTo(1);

            await using (var dueDb = new NoCtfDbContext(options))
            {
                var runtime = await dueDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.NextCheckerDueAt = fixture.Now.AddSeconds(2);
                await dueDb.SaveChangesAsync(cancellationToken);
                var schedule = await dueDb.DurableMaintenanceSchedules.SingleAsync(
                    item => item.Kind == NoCTF.Domain.Platform.MaintenanceChainKind.AwdCheckerDispatch,
                    cancellationToken);
                var outcome = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now.AddSeconds(2), schedule.ProcessingVersion),
                    dueDb, catalog, outbox, cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            var second = outbox.NodeMessages.OfType<RunAwdChecker>()
                .Single(message => message.CheckerSequence == 2);
            await Assert.That(second.CompetitionConfigurationRevision).IsEqualTo(0);
            await Assert.That(second.CompetitionChallengeRevision).IsEqualTo(0);
            await using (var downDb = new NoCtfDbContext(options))
            {
                var store = new InternalResultStore(downDb, outbox);
                var down = AwdCheckResult.Create(
                    fixture.RuntimeId, 3, 2, 7, AwdServiceState.Down, fixture.Now.AddSeconds(3));
                await Assert.That(await store.RecordAwdAsync(down, cancellationToken))
                    .IsEqualTo(InternalResultDisposition.Applied);
                var conflicting = AwdCheckResult.Create(
                    fixture.RuntimeId, 3, 2, 7, AwdServiceState.Up, fixture.Now.AddSeconds(4));
                await Assert.That(await store.RecordAwdAsync(conflicting, cancellationToken))
                    .IsEqualTo(InternalResultDisposition.Conflict);
            }

            await using (var thirdDispatchDb = new NoCtfDbContext(options))
            {
                var runtime = await thirdDispatchDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.NextCheckerDueAt = fixture.Now.AddSeconds(4);
                await thirdDispatchDb.SaveChangesAsync(cancellationToken);
                var schedule = await thirdDispatchDb.DurableMaintenanceSchedules.SingleAsync(
                    item => item.Kind == NoCTF.Domain.Platform.MaintenanceChainKind.AwdCheckerDispatch,
                    cancellationToken);
                await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now.AddSeconds(4), schedule.ProcessingVersion),
                    thirdDispatchDb, catalog, outbox, cancellationToken);
            }
            var third = outbox.NodeMessages.OfType<RunAwdChecker>()
                .Single(message => message.CheckerSequence == 3);
            await using (var deadlineDb = new NoCtfDbContext(options))
            {
                var schedule = await deadlineDb.DurableMaintenanceSchedules.SingleAsync(
                    item => item.Kind == NoCTF.Domain.Platform.MaintenanceChainKind.AwdCheckerDispatch,
                    cancellationToken);
                await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(third.Deadline, schedule.ProcessingVersion),
                    deadlineDb, catalog, outbox, cancellationToken);
            }
            await Assert.That(outbox.Published.OfType<AwdCheckerCallbackMissing>().Count()).IsEqualTo(1);

            var configurationUpdatedAt = fixture.Now.AddSeconds(20);
            await using (var configurationDb = new NoCtfDbContext(options))
            {
                var store = new ChallengeConfigurationStore(configurationDb, outbox);
                var update = await store.TryUpdateAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    expectedRevision: 0,
                    expectedCompetitionConfigurationRevision: 0,
                    JsonSerializer.Serialize(new AwdChallengeConfiguration(
                        AwdChallengeConfiguration.CurrentSchemaVersion,
                        Checker: new AwdCheckerConfiguration(
                            new RunnerJobConfiguration(
                                RuntimeProvider.Docker,
                                "checker:v2",
                                ["/checker"],
                                TimeoutSeconds: 10),
                            new ContainerAwdCheckerTarget(
                                "http://{HOST}:{PORT}/health",
                                8080))), JsonOptions),
                    configurationUpdatedAt,
                    cancellationToken);
                await Assert.That(update.Failure).IsNull();
            }
            await using (var replacementDb = new NoCtfDbContext(options))
            {
                var schedule = await replacementDb.DurableMaintenanceSchedules.SingleAsync(
                    item => item.Kind == NoCTF.Domain.Platform.MaintenanceChainKind.AwdCheckerDispatch,
                    cancellationToken);
                await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(configurationUpdatedAt, schedule.ProcessingVersion),
                    replacementDb, catalog, outbox, cancellationToken);
            }
            var replacement = outbox.NodeMessages.OfType<RunAwdChecker>()
                .Single(message => message.CheckerSequence == 5);
            await Assert.That(replacement.CompetitionChallengeRevision).IsEqualTo(1);

            await using var verify = new NoCtfDbContext(options);
            var events = await verify.ScoringEvents.AsNoTracking()
                .Where(item => item.Kind == ScoringEventKind.AwdServiceStatus)
                .ToListAsync(cancellationToken);
            await Assert.That(events).HasSingleItem();
            await Assert.That(events[0].Result).IsEqualTo(ScoringResult.Wrong);
            var runtimeState = await verify.RuntimeInstances.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(runtimeState.CheckerSequence).IsEqualTo(5);
            await Assert.That(runtimeState.LastAppliedCheckerSequence).IsEqualTo(4);
            await Assert.That(runtimeState.CheckerDeadlineAt).IsNotNull();
            await Assert.That(second.Deadline).IsEqualTo(fixture.Now.AddSeconds(12));
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        db.Users.AddRange(
            NewUser(ownerId, "owner", now),
            NewUser(memberId, "member", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWD checker",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            ConfigurationJson = JsonSerializer.Serialize(AwdConfiguration.Default, JsonOptions),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            NormalizedName = "TEAM",
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            ConfigurationJson = JsonSerializer.Serialize(new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                Checker: new AwdCheckerConfiguration(
                    new RunnerJobConfiguration(
                        RuntimeProvider.Docker,
                        "checker:latest",
                        ["/checker"],
                        TimeoutSeconds: 10),
                    new ContainerAwdCheckerTarget(
                        "http://{HOST}:{PORT}/health",
                        8080))), JsonOptions),
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Generation = 3,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "awd",
            RunnerId = "runner-a",
            State = RuntimeState.Running,
            ProcessingVersion = 7,
            ProviderReceiptJson = "{}",
            AwdCheckerTargetUrl = "http://service.internal/health",
            NextCheckerDueAt = now,
            CreatedAt = now,
            RunningAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, competitionChallengeId, runtimeId);
    }

    private static User NewUser(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<InternalResultDisposition> RecordAsync(
        DbContextOptions<NoCtfDbContext> options,
        ITransactionalMessageOutbox outbox,
        AwdCheckResult result,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var store = new InternalResultStore(db, outbox);
        return await store.RecordAwdAsync(result, cancellationToken);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid RuntimeId);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> NodeMessages { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            NodeMessages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
