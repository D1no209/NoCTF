using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdCheckerPersistenceTests
{
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
            await using (var callbackDb = new NoCtfDbContext(options))
            {
                var store = new EfInternalResultStore(callbackDb, new AwdpCheckExitCodeMapper(), outbox);
                var up = AwdCheckResult.Create(
                    fixture.RuntimeId, 3, 1, 7, AwdServiceState.Up, fixture.Now.AddSeconds(1));
                await Assert.That(await store.RecordAwdAsync(up, cancellationToken))
                    .IsEqualTo(InternalResultDisposition.Applied);
                await Assert.That(await store.RecordAwdAsync(up, cancellationToken))
                    .IsEqualTo(InternalResultDisposition.Duplicate);
            }

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
            await using (var downDb = new NoCtfDbContext(options))
            {
                var store = new EfInternalResultStore(downDb, new AwdpCheckExitCodeMapper(), outbox);
                var down = AwdCheckResult.Create(
                    fixture.RuntimeId, 3, 2, 7, AwdServiceState.Down, fixture.Now.AddSeconds(3));
                await Assert.That(await store.RecordAwdAsync(down, cancellationToken))
                    .IsEqualTo(InternalResultDisposition.Applied);
                var conflicting = AwdCheckResult.Create(
                    fixture.RuntimeId, 3, 2, 7, AwdServiceState.Up, fixture.Now.AddSeconds(4));
                await Assert.That(await store.RecordAwdAsync(conflicting, cancellationToken))
                    .IsEqualTo(InternalResultDisposition.Conflict);
            }

            await using var verify = new NoCtfDbContext(options);
            var events = await verify.ScoringEvents.AsNoTracking()
                .Where(item => item.Kind == ScoringEventKind.AwdServiceStatus)
                .ToListAsync(cancellationToken);
            await Assert.That(events).HasSingleItem();
            await Assert.That(events[0].Result).IsEqualTo(ScoringResult.Wrong);
            var runtimeState = await verify.RuntimeInstances.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(runtimeState.LastAppliedCheckerSequence).IsEqualTo(2);
            await Assert.That(runtimeState.CheckerDeadlineAt).IsNull();
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
            ConfigurationJson = JsonSerializer.Serialize(AwdConfiguration.Default),
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
                Checker: new RunnerJobConfiguration(
                    RuntimeProvider.Docker,
                    "checker:latest",
                    ["/checker"],
                    TimeoutSeconds: 10))),
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
            ControlCheckUrl = "http://service.internal/health",
            NextCheckerDueAt = now,
            CreatedAt = now,
            RunningAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, runtimeId);
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

    private sealed record Fixture(DateTimeOffset Now, Guid RuntimeId);

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
