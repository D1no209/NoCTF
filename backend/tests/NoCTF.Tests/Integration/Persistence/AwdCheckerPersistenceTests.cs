using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
[Category("AwdChecker")]
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
                    new(fixture.Now), dispatchDb, catalog, outbox, cancellationToken);
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
                var outcome = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now.AddSeconds(1)),
                    inflightDb, catalog, outbox, cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Idempotent);
                await Assert.That(outbox.NodeMessages.OfType<RunAwdChecker>().Count()).IsEqualTo(1);
            }
            var up = AwdCheckResult.Create(
                fixture.RuntimeId,
                3,
                first.CheckerSequence,
                first.ProcessingVersion,
                AwdServiceState.Up,
                fixture.Now.AddSeconds(1));
            var concurrentCallbacks = await Task.WhenAll(
                RecordAsync(options, outbox, up, cancellationToken),
                RecordAsync(options, outbox, up, cancellationToken));
            await Assert.That(concurrentCallbacks.Count(
                outcome => outcome == InternalResultDisposition.Applied)).IsEqualTo(2);

            await using (var dueDb = new NoCtfDbContext(options))
            {
                var runtime = await dueDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.NextCheckerDueAt = fixture.Now.AddSeconds(2);
                await dueDb.SaveChangesAsync(cancellationToken);
                var outcome = await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now.AddSeconds(2)),
                    dueDb, catalog, outbox, cancellationToken);
                await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
            }

            var second = outbox.NodeMessages.OfType<RunAwdChecker>()
                .Single(message => message.CheckerSequence == 2);
            await Assert.That(second.CompetitionConfigurationRevision).IsEqualTo(0);
            await Assert.That(second.CompetitionChallengeRevision).IsEqualTo(0);
            var down = AwdCheckResult.Create(
                fixture.RuntimeId,
                3,
                second.CheckerSequence,
                second.ProcessingVersion,
                AwdServiceState.Down,
                fixture.Now.AddSeconds(4));
            await Assert.That(await RecordAsync(
                    options, outbox, down, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Applied);
            var later = AwdCheckResult.Create(
                fixture.RuntimeId,
                3,
                second.CheckerSequence,
                second.ProcessingVersion,
                AwdServiceState.Up,
                fixture.Now.AddSeconds(3));
            await Assert.That(await RecordAsync(
                    options, outbox, later, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Applied);

            await using (var thirdDispatchDb = new NoCtfDbContext(options))
            {
                var runtime = await thirdDispatchDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.NextCheckerDueAt = fixture.Now.AddSeconds(4);
                await thirdDispatchDb.SaveChangesAsync(cancellationToken);
                await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(fixture.Now.AddSeconds(4)),
                    thirdDispatchDb, catalog, outbox, cancellationToken);
            }
            var third = outbox.NodeMessages.OfType<RunAwdChecker>()
                .Single(message => message.CheckerSequence == 3);
            await Assert.That(await RecordAsync(
                    options, outbox, down, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Superseded);
            var staleProcessingVersion = AwdCheckResult.Create(
                fixture.RuntimeId,
                3,
                third.CheckerSequence,
                third.ProcessingVersion - 1,
                AwdServiceState.Down,
                fixture.Now.AddSeconds(5));
            await Assert.That(await RecordAsync(
                    options, outbox, staleProcessingVersion, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Superseded);
            var futureProcessingVersion = staleProcessingVersion with
            {
                ProcessingVersion = third.ProcessingVersion + 1
            };
            await Assert.That(await RecordAsync(
                    options, outbox, futureProcessingVersion, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Conflict);
            var futureCheckerSequence = staleProcessingVersion with
            {
                CheckerSequence = third.CheckerSequence + 1,
                ProcessingVersion = third.ProcessingVersion
            };
            await Assert.That(await RecordAsync(
                    options, outbox, futureCheckerSequence, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Conflict);
            var mixedFence = futureCheckerSequence with
            {
                ProcessingVersion = third.ProcessingVersion - 1
            };
            await Assert.That(await RecordAsync(
                    options, outbox, mixedFence, cancellationToken))
                .IsEqualTo(InternalResultDisposition.Conflict);
            await using (var fenceDb = new NoCtfDbContext(options))
            {
                var runtime = await fenceDb.RuntimeInstances.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(runtime.CheckerStatus).IsEqualTo(AwdServiceState.Up);
                var expectedUpdatedAt = down.OccurredAt.AddTicks(
                    -(down.OccurredAt.Ticks % TimeSpan.TicksPerMicrosecond)
                    + TimeSpan.TicksPerMicrosecond);
                await Assert.That(runtime.CheckerStatusUpdatedAt)
                    .IsEqualTo(expectedUpdatedAt);
                await Assert.That(runtime.LastAppliedCheckerSequence)
                    .IsEqualTo(second.CheckerSequence);
                var expectedDeadline = third.Deadline.AddTicks(
                    -(third.Deadline.Ticks % TimeSpan.TicksPerMicrosecond));
                await Assert.That(runtime.CheckerDeadlineAt).IsEqualTo(expectedDeadline);
                var scoringEvents = await fenceDb.ScoringEvents.AsNoTracking()
                    .Where(item => item.Kind == ScoringEventKind.AwdServiceStatus)
                    .OrderBy(item => item.OccurredAt)
                    .ToListAsync(cancellationToken);
                await Assert.That(scoringEvents).Count().IsEqualTo(2);
                await Assert.That(scoringEvents.Select(item => item.Result))
                    .IsEquivalentTo([ScoringResult.Wrong, ScoringResult.Correct]);
                await Assert.That(scoringEvents.Select(item => item.ProcessingVersion))
                    .IsEquivalentTo(new long?[] { null, null });
                var leaderboardRevision = await fenceDb.Competitions.AsNoTracking()
                    .Select(competition => competition.LeaderboardRevision)
                    .SingleAsync(cancellationToken);
                await Assert.That(leaderboardRevision).IsEqualTo(2);
                await Assert.That(outbox.Published.OfType<InvalidateLeaderboard>().Count())
                    .IsEqualTo(2);
            }
            await using (var deadlineDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(third.Deadline),
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
                        CheckerIntervalSeconds: 15), JsonOptions),
                    configurationUpdatedAt,
                    cancellationToken);
                await Assert.That(update.Failure).IsNull();
            }
            await using (var replacementDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.ExecuteAwdCheckerDispatchAsync(
                    new(configurationUpdatedAt),
                    replacementDb, catalog, outbox, cancellationToken);
            }
            var replacement = outbox.NodeMessages.OfType<RunAwdChecker>()
                .Single(message => message.CheckerSequence == 5);
            await Assert.That(replacement.CompetitionChallengeRevision).IsEqualTo(1);

            await using var verify = new NoCtfDbContext(options);
            var events = await verify.ScoringEvents.AsNoTracking()
                .Where(item => item.Kind == ScoringEventKind.AwdServiceStatus)
                .OrderBy(item => item.OccurredAt)
                .ToListAsync(cancellationToken);
            await Assert.That(events).Count().IsEqualTo(2);
            await Assert.That(events[0].Result).IsEqualTo(ScoringResult.Wrong);
            await Assert.That(events[1].Result).IsEqualTo(ScoringResult.Correct);
            var runtimeState = await verify.RuntimeInstances.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(runtimeState.CheckerSequence).IsEqualTo(5);
            await Assert.That(runtimeState.LastAppliedCheckerSequence).IsEqualTo(4);
            await Assert.That(runtimeState.CheckerDeadlineAt).IsNotNull();
            await Assert.That(second.Deadline).IsEqualTo(fixture.Now.AddSeconds(12));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_runtime_callbacks_increment_the_shared_revision_atomically(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awd_checker_revision")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var revisionReadBarrier = new CompetitionRevisionReadBarrier();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(revisionReadBarrier)
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var secondRuntimeId = await AddConcurrentRuntimeAsync(
                options,
                fixture,
                cancellationToken);
            revisionReadBarrier.Enable();
            var outbox = new RecordingOutbox();
            var first = AwdCheckResult.Create(
                fixture.RuntimeId,
                3,
                checkerSequence: 1,
                processingVersion: 7,
                AwdServiceState.Down,
                fixture.Now.AddSeconds(1));
            var second = AwdCheckResult.Create(
                secondRuntimeId,
                3,
                checkerSequence: 1,
                processingVersion: 7,
                AwdServiceState.Down,
                fixture.Now.AddSeconds(1));

            var dispositions = await Task.WhenAll(
                RecordAsync(options, outbox, first, cancellationToken),
                RecordAsync(options, outbox, second, cancellationToken));

            await Assert.That(dispositions.Count(
                disposition => disposition == InternalResultDisposition.Applied))
                .IsEqualTo(2);
            await Assert.That(revisionReadBarrier.Arrivals).IsEqualTo(2);
            await using var verify = new NoCtfDbContext(options);
            var scoringEvents = await verify.ScoringEvents.AsNoTracking()
                .Where(item => item.Kind == ScoringEventKind.AwdServiceStatus)
                .ToListAsync(cancellationToken);
            await Assert.That(scoringEvents).Count().IsEqualTo(2);
            await Assert.That(scoringEvents.All(
                scoringEvent => scoringEvent.Result == ScoringResult.Wrong)).IsTrue();
            await Assert.That(scoringEvents.Select(scoringEvent => scoringEvent.TeamId).Distinct())
                .Count().IsEqualTo(2);
            var leaderboardRevision = await verify.Competitions.AsNoTracking()
                .Select(competition => competition.LeaderboardRevision)
                .SingleAsync(cancellationToken);
            await Assert.That(leaderboardRevision).IsEqualTo(2);
            await Assert.That(outbox.Published.OfType<InvalidateLeaderboard>().Count())
                .IsEqualTo(2);
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
            Mode = GameMode.Awd,
            Title = "service",
            DefinitionJson = JsonSerializer.Serialize(new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                Checker: new AwdCheckerConfiguration(
                    new RunnerJobConfiguration("checker:latest",
                        ["/checker"],
                        TimeoutSeconds: 10))), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion), JsonOptions),
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
            AwdCheckerTargetHost = "service.internal",
            NextCheckerDueAt = now,
            CreatedAt = now,
            RunningAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, competitionChallengeId, runtimeId);
    }

    private static async Task<Guid> AddConcurrentRuntimeAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var first = await db.RuntimeInstances.SingleAsync(
            runtime => runtime.Id == fixture.RuntimeId,
            cancellationToken);
        first.CheckerSequence = 1;
        first.CheckerStatus = AwdServiceState.Up;
        first.CheckerStatusUpdatedAt = fixture.Now;
        first.CheckerDeadlineAt = fixture.Now.AddSeconds(10);

        var userId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        db.Users.Add(NewUser(userId, "member-two", fixture.Now));
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = fixture.CompetitionId,
            Name = "team-two",
            NormalizedName = "TEAM-TWO",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('b', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = fixture.Now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = teamId,
            Generation = 3,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "awd",
            RunnerId = "runner-b",
            State = RuntimeState.Running,
            ProcessingVersion = 7,
            ProviderReceiptJson = "{}",
            AwdCheckerTargetHost = "service.internal",
            CheckerStatus = AwdServiceState.Up,
            CheckerStatusUpdatedAt = fixture.Now,
            CheckerSequence = 1,
            CheckerDeadlineAt = fixture.Now.AddSeconds(10),
            CreatedAt = fixture.Now,
            RunningAt = fixture.Now
        });
        await db.SaveChangesAsync(cancellationToken);
        return runtimeId;
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

    private sealed class CompetitionRevisionReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        private int enabled;

        public int Arrivals => Volatile.Read(ref arrivals);

        public void Enable() => Volatile.Write(ref enabled, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref enabled) == 0
                || !command.CommandText.TrimStart().StartsWith(
                    "SELECT",
                    StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(
                    "competition_challenges",
                    StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(
                    "competitions",
                    StringComparison.OrdinalIgnoreCase))
                return result;

            if (Interlocked.Increment(ref arrivals) == 2)
                release.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        private readonly object sync = new();

        public List<object> Published { get; } = [];
        public List<object> NodeMessages { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            lock (sync)
                Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            lock (sync)
                Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            lock (sync)
                NodeMessages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
