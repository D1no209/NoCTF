using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Infrastructure.Runtime.Instances;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[NotInParallel]
public sealed class RuntimeQuotaPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Admin_termination_targets_the_exact_instance_and_is_idempotent(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runtime_exact_termination")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres.GetConnectionString());
            var fixture = await SeedAsync(options, [RuntimeState.Running], cancellationToken);
            var actorUserId = fixture.Teams[0].UserId;
            Guid runtimeInstanceId;
            await using (var lookup = new NoCtfDbContext(options))
            {
                runtimeInstanceId = await lookup.RuntimeInstances.AsNoTracking()
                    .Where(runtime => runtime.CompetitionId == fixture.CompetitionId)
                    .Select(runtime => runtime.Id)
                    .SingleAsync(cancellationToken);
            }

            var outbox = new RecordingOutbox();
            var events = new RecordingCompetitionEventRecorder();
            await using (var db = new NoCtfDbContext(options))
            {
                var store = CreateAdminStore(db, outbox, events);
                var result = await store.TerminateAsync(
                    fixture.CompetitionId,
                    runtimeInstanceId,
                    actorUserId,
                    fixture.Now,
                    cancellationToken);

                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Runtime!.Id).IsEqualTo(runtimeInstanceId);
                await Assert.That(result.Runtime.State).IsEqualTo(RuntimeState.Stopping);
            }

            await Assert.That(outbox.Published.OfType<StopRuntime>().Single())
                .IsEqualTo(new StopRuntime(runtimeInstanceId));
            var recorded = events.Drafts.Single();
            await Assert.That(recorded.ActorUserId).IsEqualTo(actorUserId);
            await Assert.That(recorded.RuntimeInstanceId).IsEqualTo(runtimeInstanceId);
            await Assert.That(recorded.RuntimeState).IsEqualTo(RuntimeState.Stopping);

            await using (var retryDb = new NoCtfDbContext(options))
            {
                var retryOutbox = new RecordingOutbox();
                var retry = await CreateAdminStore(retryDb, retryOutbox).TerminateAsync(
                    fixture.CompetitionId,
                    runtimeInstanceId,
                    actorUserId,
                    fixture.Now.AddSeconds(1),
                    cancellationToken);

                await Assert.That(retry.Failure).IsNull();
                await Assert.That(retry.Runtime).IsNotNull();
                await Assert.That(retry.Runtime!.Id).IsEqualTo(runtimeInstanceId);
                await Assert.That(retry.Runtime.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(retryOutbox.Published).IsEmpty();
            }

            await using (var assignmentDb = new NoCtfDbContext(options))
            {
                var assigned = await assignmentDb.RuntimeInstances.SingleAsync(
                    runtime => runtime.Id == runtimeInstanceId,
                    cancellationToken);
                assigned.RunnerId = "runner-a";
                await assignmentDb.SaveChangesAsync(cancellationToken);
            }

            var forceOutbox = new RecordingOutbox();
            var forceEvents = new RecordingCompetitionEventRecorder();
            await using (var forceDb = new NoCtfDbContext(options))
            {
                var forced = await CreateAdminStore(forceDb, forceOutbox, forceEvents)
                    .ForceTerminateAsync(
                        fixture.CompetitionId,
                        runtimeInstanceId,
                        actorUserId,
                        "Runner cleanup exceeded the operation timeout.",
                        fixture.Now.Add(RuntimeForceTerminationPolicy.StuckThreshold),
                        cancellationToken);

                await Assert.That(forced.Failure).IsNull();
            }

            var forceMessage = forceOutbox.Published.OfType<ForceTerminateRuntime>().Single();
            await Assert.That(forceMessage.RuntimeInstanceId).IsEqualTo(runtimeInstanceId);
            await Assert.That(forceMessage.RunnerId).IsEqualTo("runner-a");
            var forceEvent = forceEvents.Drafts.Single();
            await Assert.That(forceEvent.Kind)
                .IsEqualTo(CompetitionEventKind.RuntimeForceTerminationRequested);
            await Assert.That(forceEvent.RuntimeCleanupResult)
                .IsEqualTo(RuntimeCleanupResult.Pending);
            await Assert.That(forceEvent.Reason)
                .IsEqualTo("Runner cleanup exceeded the operation timeout.");
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_player_and_admin_starts_do_not_exceed_the_team_quota(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runtime_quota_concurrency")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            var options = Options(connectionString);
            var fixture = await SeedAsync(options, [], cancellationToken);
            var playerStart = MutatePlayerAsync(
                options,
                fixture,
                fixture.Teams[0].UserId,
                fixture.ChallengeIds[0],
                RuntimeAction.Start,
                cancellationToken);
            var adminStart = MutateAdminAsync(
                options,
                fixture,
                fixture.Teams[0].TeamId,
                fixture.ChallengeIds[1],
                RuntimeAction.Start,
                cancellationToken);

            var attempts = await Task.WhenAll(playerStart, adminStart)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            await Assert.That(attempts.Count(attempt => attempt.Result.Runtime is not null))
                .IsEqualTo(1);
            await Assert.That(attempts.Count(attempt =>
                    attempt.Result.Failure == RuntimeMutationFailure.CapacityExceeded))
                .IsEqualTo(1);
            await Assert.That(attempts.Sum(attempt => attempt.DispatchCount)).IsEqualTo(1);

            await using var verify = new NoCtfDbContext(options);
            await Assert.That(await verify.RuntimeInstances.AsNoTracking()
                    .CountAsync(runtime =>
                        runtime.CompetitionId == fixture.CompetitionId &&
                        runtime.TeamId == fixture.Teams[0].TeamId,
                        cancellationToken))
                .IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Active_states_consume_slots_terminal_states_do_not_and_reset_keeps_one_logical_slot(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runtime_quota_states")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres.GetConnectionString());
            RuntimeState[] states =
            [
                RuntimeState.Queued,
                RuntimeState.Provisioning,
                RuntimeState.Running,
                RuntimeState.Stopping,
                RuntimeState.Stopped,
                RuntimeState.Failed
            ];
            var fixture = await SeedAsync(options, states, cancellationToken);

            foreach (var state in states[..4])
            {
                var team = fixture.Teams.Single(candidate => candidate.State == state);
                var attempt = await MutatePlayerAsync(
                    options,
                    fixture,
                    team.UserId,
                    fixture.ChallengeIds[1],
                    RuntimeAction.Start,
                    cancellationToken);
                await Assert.That(attempt.Result.Failure)
                    .IsEqualTo(RuntimeMutationFailure.CapacityExceeded);
                await Assert.That(attempt.DispatchCount).IsEqualTo(0);
            }

            foreach (var state in states[4..])
            {
                var team = fixture.Teams.Single(candidate => candidate.State == state);
                var attempt = await MutatePlayerAsync(
                    options,
                    fixture,
                    team.UserId,
                    fixture.ChallengeIds[1],
                    RuntimeAction.Start,
                    cancellationToken);
                await Assert.That(attempt.Result.Failure).IsNull();
                await Assert.That(attempt.Result.Runtime).IsNotNull();
                await Assert.That(attempt.DispatchCount).IsEqualTo(1);
            }

            var runningTeam = fixture.Teams.Single(team => team.State == RuntimeState.Running);
            Guid previousRuntimeId;
            await using (var beforeReset = new NoCtfDbContext(options))
            {
                previousRuntimeId = await beforeReset.RuntimeInstances.AsNoTracking()
                    .Where(runtime => runtime.TeamId == runningTeam.TeamId
                        && runtime.CompetitionChallengeId == fixture.ChallengeIds[0])
                    .Select(runtime => runtime.Id)
                    .SingleAsync(cancellationToken);
            }
            var reset = await MutatePlayerAsync(
                options,
                fixture,
                runningTeam.UserId,
                fixture.ChallengeIds[0],
                RuntimeAction.Reset,
                cancellationToken);
            await Assert.That(reset.Result.Failure).IsNull();
            await Assert.That(reset.Result.Runtime!.Id).IsNotEqualTo(previousRuntimeId);

            var afterReset = await MutatePlayerAsync(
                options,
                fixture,
                runningTeam.UserId,
                fixture.ChallengeIds[1],
                RuntimeAction.Start,
                cancellationToken);
            await Assert.That(afterReset.Result.Failure)
                .IsEqualTo(RuntimeMutationFailure.CapacityExceeded);

            await using var verify = new NoCtfDbContext(options);
            var activeRows = await verify.RuntimeInstances.AsNoTracking()
                .Where(runtime =>
                    runtime.CompetitionId == fixture.CompetitionId &&
                    runtime.TeamId == runningTeam.TeamId &&
                    (runtime.State == RuntimeState.Queued ||
                     runtime.State == RuntimeState.Provisioning ||
                     runtime.State == RuntimeState.Running ||
                     runtime.State == RuntimeState.Stopping))
                .ToListAsync(cancellationToken);
            await Assert.That(activeRows).Count().IsEqualTo(2);
            await Assert.That(activeRows.Select(runtime => runtime.CompetitionChallengeId).Distinct())
                .Count().IsEqualTo(1);
        });
    }

    private static DbContextOptions<NoCtfDbContext> Options(
        string connectionString,
        params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors)
            .Options;

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        IReadOnlyList<RuntimeState> states,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddTicks(1));
        var challengeIds = new[]
        {
            Guid.CreateVersion7(now.AddTicks(2)),
            Guid.CreateVersion7(now.AddTicks(3))
        };
        db.Users.Add(User(ownerId, "quota-owner", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Runtime quota",
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddChallenge(db, ownerId, competitionId, challengeIds[0], "First", 1, now);
        AddChallenge(db, ownerId, competitionId, challengeIds[1], "Second", 2, now);

        var seededStates = states.Count == 0
            ? new RuntimeState?[] { null }
            : states.Select(state => (RuntimeState?)state).ToArray();
        var teams = new List<TeamFixture>();
        for (var index = 0; index < seededStates.Length; index++)
        {
            var state = seededStates[index];
            var userId = Guid.CreateVersion7(now.AddTicks(10 + index * 3));
            var teamId = Guid.CreateVersion7(now.AddTicks(11 + index * 3));
            db.Users.Add(User(userId, $"quota-player-{index}", now));
            db.Teams.Add(new Team
            {
                Id = teamId,
                CompetitionId = competitionId,
                Name = $"Quota Team {index}",
                CaptainId = userId,
                MemberIds = [userId],
                InvitationToken = index.ToString().PadLeft(32, 'a'),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            });
            if (state is RuntimeState runtimeState)
            {
                db.RuntimeInstances.Add(new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(now.AddTicks(12 + index * 3)),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = challengeIds[0],
                    TeamId = teamId,
                    Purpose = RuntimePurpose.Player,
                    RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = RuntimeProvider.Docker,
                    State = runtimeState,
                    FailureCode = runtimeState == RuntimeState.Failed
                        ? RuntimeFailureCode.ProviderUnavailable
                        : null,
                    CreatedAt = now,
                    RunningAt = runtimeState == RuntimeState.Running ? now : null,
                    StoppedAt = runtimeState == RuntimeState.Stopped ? now : null
                });
            }
            teams.Add(new(userId, teamId, state));
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, challengeIds, teams);
    }

    private static void AddChallenge(
        NoCtfDbContext db,
        Guid ownerId,
        Guid competitionId,
        Guid competitionChallengeId,
        string title,
        int order,
        DateTimeOffset now)
    {
        var templateId = Guid.CreateVersion7();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/quota:v1"),
            new RuntimeResourceLimits(67_108_864, 100_000_000, 64));
        db.Challenges.Add(new Challenge
        {
            Id = templateId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = title,
            DefinitionJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    Runtime: runtime),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = templateId,
            Order = order,
            IsPublished = true,
            RulesJson = new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Ctf),
            UpdatedAt = now
        });
    }

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

    private static async Task<MutationAttempt> MutatePlayerAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        Guid userId,
        Guid competitionChallengeId,
        RuntimeAction action,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var outbox = new RecordingOutbox();
        var store = new RuntimeInstanceStore(
            db,
            new ChallengeRuntimeTemplateCatalog(),
            new FixedRuntimePlacementPolicy(runnerPool: "quota-tests"),
            new PostgresPerTeamRuntimeFlagStore(db),
            outbox);
        var result = await store.MutatePlayerRuntimeAsync(
            new(
                fixture.CompetitionId,
                competitionChallengeId,
                userId,
                action,
                null,
                fixture.Now),
            cancellationToken);
        return new(result, outbox.Published.OfType<DispatchRuntime>().Count());
    }

    private static async Task<MutationAttempt> MutateAdminAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        Guid teamId,
        Guid competitionChallengeId,
        RuntimeAction action,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var outbox = new RecordingOutbox();
        var store = new AdminRuntimeStore(
            db,
            new ChallengeRuntimeTemplateCatalog(),
            new FixedRuntimePlacementPolicy(runnerPool: "quota-tests"),
            new PostgresPerTeamRuntimeFlagStore(db),
            outbox);
        var result = await store.MutateAsync(
            fixture.CompetitionId,
            competitionChallengeId,
            teamId,
            action,
            null,
            fixture.Now,
            cancellationToken);
        return new(result, outbox.Published.OfType<DispatchRuntime>().Count());
    }

    private static AdminRuntimeStore CreateAdminStore(
        NoCtfDbContext db,
        RecordingOutbox outbox,
        ICompetitionEventRecorder? events = null) =>
        new(
            db,
            new ChallengeRuntimeTemplateCatalog(),
            new FixedRuntimePlacementPolicy(runnerPool: "quota-tests"),
            new PostgresPerTeamRuntimeFlagStore(db),
            outbox,
            events);

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        IReadOnlyList<Guid> ChallengeIds,
        IReadOnlyList<TeamFixture> Teams);

    private sealed record TeamFixture(Guid UserId, Guid TeamId, RuntimeState? State);

    private sealed record MutationAttempt(RuntimeMutationResult Result, int DispatchCount);

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

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class RecordingCompetitionEventRecorder : ICompetitionEventRecorder
    {
        public List<CompetitionEventDraft> Drafts { get; } = [];

        public ValueTask<Guid> RecordAsync(
            CompetitionEventDraft draft,
            CancellationToken cancellationToken = default)
        {
            Drafts.Add(draft);
            return ValueTask.FromResult(Guid.CreateVersion7(draft.OccurredAt));
        }
    }
}
