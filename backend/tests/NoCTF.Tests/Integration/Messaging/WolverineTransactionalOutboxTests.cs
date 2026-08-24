using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using JasperFx;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Postgresql;
using Wolverine.Runtime;
using LifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using LifecycleMessage = NoCTF.Application.Messaging.AdvanceCompetitionLifecycle;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("WolverineTransactionalOutbox")]
[NotInParallel]
public sealed class WolverineTransactionalOutboxTests
{
    [Test]
    [Category("AwdpFixFence")]
    [Timeout(300_000)]
    public async Task Awdp_nonzero_checker_exit_is_persisted_through_wolverine(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var fixture = AwdpFixExecutionFixture.Create(exitCode: 17);
            using var host = BuildHost(
                postgres.GetConnectionString(),
                awdpFixExecution: fixture);
            await SeedAwdpFixAsync(host, fixture, cancellationToken);
            await host.StartAsync(cancellationToken);
            try
            {
                var observation = AwdpFixResultObservation.Expect(fixture.GameplayFactId);
                await host.Services.GetRequiredService<IMessageBus>()
                    .SendAsync(fixture.Message);
                var recorded = await observation.Task.WaitAsync(cancellationToken);

                await WaitForGameplayFactStateAsync(
                    host,
                    fixture.GameplayFactId,
                    GameplayFactState.Completed,
                    cancellationToken);
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var fact = await db.GameplayFacts.AsNoTracking()
                    .SingleAsync(
                        item => item.Id == fixture.GameplayFactId,
                        cancellationToken);
                var runtime = await db.RuntimeInstances.AsNoTracking()
                    .SingleAsync(
                        item => item.Id == fixture.RuntimeInstanceId,
                        cancellationToken);
                await Assert.That(recorded.Message.Outcome)
                    .IsEqualTo(AwdpFixOutcome.PlatformFailed);
                await Assert.That(recorded.Disposition)
                    .IsEqualTo(InternalResultDisposition.Applied);
                await Assert.That(fixture.OneShotRunner.ExecutionCount).IsEqualTo(1);
                await Assert.That(AwdpFixResultObservation.Count(fixture.GameplayFactId))
                    .IsEqualTo(1);
                await Assert.That(fact.FailureCode)
                    .IsEqualTo(GameplayFactFailureCode.AwdpPlatformFailed);
                await Assert.That(fact.Result)
                    .IsEqualTo(GameplayFactResult.Rejected);
                await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopping);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Category("AwdpFixFence")]
    [Timeout(300_000)]
    public async Task Awdp_replay_message_preserves_its_fence_across_wolverine_retry(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            using var host = BuildHost(postgres.GetConnectionString());
            await host.StartAsync(cancellationToken);
            try
            {
                var message = new CompleteAwdpFixRecovery(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "test-runner",
                    DateTimeOffset.UtcNow);
                var observation = AwdpReplayProbeObservation.Expect(message.GameplayFactId);

                await host.Services.GetRequiredService<IMessageBus>().SendAsync(message);

                var received = await observation.Task.WaitAsync(cancellationToken);
                await Assert.That(received).IsEqualTo(message);
                await Assert.That(AwdpReplayProbeObservation.Attempts(message.GameplayFactId))
                    .IsEqualTo(2);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Maintenance_ticks_are_single_active_and_fail_over_between_workers(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            const string firstHostId = "first";
            const string secondHostId = "second";
            using var first = BuildMaintenanceHost(connectionString, firstHostId);
            using var second = BuildMaintenanceHost(connectionString, secondHostId);
            var firstStopped = false;
            var secondStopped = false;
            MaintenanceTickObservation.Reset();

            await first.StartAsync(cancellationToken);
            await second.StartAsync(cancellationToken);
            try
            {
                await WaitUntilAsync(
                    () => MaintenanceTickObservation.ActiveHostCount == 1,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                await Assert.That(MaintenanceTickObservation.ActiveHostCount).IsEqualTo(1);

                var activeHostId = MaintenanceTickObservation.ActiveHostIds.Single();
                var active = activeHostId == firstHostId ? first : second;
                var leaderboardCompetitionId = Guid.CreateVersion7();
                active.Services.GetRequiredService<LeaderboardProjectionMergeQueue>()
                    .Enqueue(leaderboardCompetitionId, DateTimeOffset.UtcNow);
                await WaitUntilAsync(
                    () => MaintenanceTickObservation.LeaderboardDispatchCount(
                        leaderboardCompetitionId) == 1,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                await Assert.That(MaintenanceTickObservation.LeaderboardDispatchCount(
                        leaderboardCompetitionId))
                    .IsEqualTo(1);

                var standbyHostId = activeHostId == firstHostId ? secondHostId : firstHostId;
                var standbyCountBeforeFailover =
                    MaintenanceTickObservation.DispatchCount(standbyHostId);
                if (ReferenceEquals(active, first))
                {
                    await first.StopAsync(cancellationToken);
                    firstStopped = true;
                }
                else
                {
                    await second.StopAsync(cancellationToken);
                    secondStopped = true;
                }

                await WaitUntilAsync(
                    () => MaintenanceTickObservation.DispatchCount(standbyHostId)
                        > standbyCountBeforeFailover,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                await Assert.That(MaintenanceTickObservation.DispatchCount(standbyHostId))
                    .IsGreaterThan(standbyCountBeforeFailover);
            }
            finally
            {
                if (!firstStopped)
                    await first.StopAsync(cancellationToken);
                if (!secondStopped)
                    await second.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Consumer_observes_business_fact_committed_with_outbox(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_wolverine_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            using var host = BuildHost(postgres.GetConnectionString());
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE outbox_business_probe (id uuid PRIMARY KEY)",
                    cancellationToken);
            }

            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var observation = OutboxProbeObservation.Expect(id);
                var bus = host.Services.GetRequiredService<IMessageBus>();

                await bus.SendAsync(new WriteOutboxBusinessProbe(id));

                await Assert.That(await observation.WaitAsync(cancellationToken)).IsTrue();
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Scheduled_outbox_message_is_not_visible_before_its_due_time(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            using var host = BuildHost(postgres.GetConnectionString());
            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var dueAt = DateTimeOffset.UtcNow.AddSeconds(1);
                var observation = ScheduledProbeObservation.Expect(id);

                await host.Services.GetRequiredService<IMessageBus>()
                    .SendAsync(new WriteScheduledOutboxProbe(id, dueAt));

                var observedAt = await observation.WaitAsync(cancellationToken);
                await Assert.That(observedAt).IsGreaterThanOrEqualTo(dueAt);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Scheduled_messages_are_owned_by_the_process_schema(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            using var runner = BuildHost(connectionString, WolverinePersistenceSchemas.Runner);
            await runner.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var dueAt = DateTimeOffset.UtcNow.AddSeconds(3);
                var observation = ScheduledProbeObservation.Expect(id);

                using (var worker = BuildHost(connectionString, WolverinePersistenceSchemas.Worker))
                {
                    await worker.StartAsync(cancellationToken);
                    try
                    {
                        await using var scope = worker.Services.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                        var outbox = scope.ServiceProvider
                            .GetRequiredService<ITransactionalMessageOutbox>();
                        await using var transaction = await db.Database.BeginTransactionAsync(
                            cancellationToken);
                        await outbox.ScheduleAsync(
                            new ObserveScheduledOutboxProbe(id),
                            dueAt);
                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        await outbox.FlushOutgoingMessagesAsync();
                    }
                    finally
                    {
                        await worker.StopAsync(cancellationToken);
                    }
                }

                var ownershipObservationDelay = dueAt + TimeSpan.FromSeconds(1)
                    - DateTimeOffset.UtcNow;
                if (ownershipObservationDelay > TimeSpan.Zero)
                    await Task.Delay(ownershipObservationDelay, cancellationToken);
                await Assert.That(observation.IsCompleted).IsFalse();

                using var replacementWorker = BuildHost(
                    connectionString,
                    WolverinePersistenceSchemas.Worker);
                await replacementWorker.StartAsync(cancellationToken);
                var observedAt = await observation.WaitAsync(cancellationToken);
                await Assert.That(observedAt).IsGreaterThanOrEqualTo(dueAt);
                await replacementWorker.StopAsync(cancellationToken);
            }
            finally
            {
                await runner.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_business_transaction_rolls_back_and_dead_letter_can_be_replayed(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            using var host = BuildHost(connectionString, WolverinePersistenceSchemas.Worker);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE rollback_business_probe (id uuid PRIMARY KEY)",
                    cancellationToken);
            }

            RollbackOutboxProbeHandler.Fail = true;
            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var replayed = RollbackProbeObservation.Expect(id);
                await host.Services.GetRequiredService<IMessageBus>()
                    .SendAsync(new WriteRollbackOutboxProbe(id));

                var deadLetters = host.Services.GetRequiredService<IWolverineRuntime>()
                    .Storage.DeadLetters;
                var deadLetter = await WaitForDeadLetterAsync(deadLetters, cancellationToken);
                await using var processDeadLetters = new WolverineProcessDeadLetters(connectionString);
                var aggregatedDeadLetter = await processDeadLetters.FindAsync(
                    deadLetter.Id,
                    cancellationToken);
                await Assert.That(aggregatedDeadLetter).IsNotNull();
                await Assert.That((await processDeadLetters.ListAsync(10, cancellationToken))
                    .Any(candidate => candidate.Id == deadLetter.Id)).IsTrue();
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    var count = await db.Database.SqlQuery<int>(
                            $"SELECT count(*)::int AS \"Value\" FROM rollback_business_probe WHERE id = {id}")
                        .SingleAsync(cancellationToken);
                    await Assert.That(count).IsEqualTo(0);
                }

                RollbackOutboxProbeHandler.Fail = false;
                await Assert.That(await processDeadLetters.ReplayAsync(
                    deadLetter.Id,
                    cancellationToken)).IsTrue();

                await Assert.That(await replayed.WaitAsync(cancellationToken)).IsTrue();
            }
            finally
            {
                RollbackOutboxProbeHandler.Fail = true;
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Lifecycle_ticks_remain_idempotent_across_worker_restart(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            using (var migrateHost = BuildHost(connectionString))
            {
                await using var scope = migrateHost.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>()
                    .Database.EnsureCreatedAsync(cancellationToken);
            }

            var firstObservation = LifecycleTickObservation.Expect(2);
            using (var firstHost = BuildHost(connectionString))
            {
                await firstHost.StartAsync(cancellationToken);
                try
                {
                    var bus = firstHost.Services.GetRequiredService<IMessageBus>();
                    var now = DateTimeOffset.UtcNow;
                    await bus.SendAsync(new LifecycleMessage(now));
                    await bus.SendAsync(new LifecycleMessage(now));
                    await firstObservation.Completed.WaitAsync(
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                    await Assert.That(firstObservation.Outcomes).Count().IsEqualTo(2);
                    await Assert.That(firstObservation.Outcomes.All(outcome =>
                        outcome == MessageExecutionOutcome.Idempotent)).IsTrue();
                }
                finally
                {
                    await firstHost.StopAsync(cancellationToken);
                }
            }

            using (var restartedHost = BuildHost(connectionString))
            {
                var restartObservation = LifecycleTickObservation.Expect(1);
                await restartedHost.StartAsync(cancellationToken);
                try
                {
                    await restartedHost.Services.GetRequiredService<IMessageBus>()
                        .SendAsync(new LifecycleMessage(DateTimeOffset.UtcNow));
                    await restartObservation.Completed.WaitAsync(
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                    await Assert.That(restartObservation.Outcomes).Count().IsEqualTo(1);
                    await Assert.That(restartObservation.Outcomes[0])
                        .IsEqualTo(MessageExecutionOutcome.Idempotent);
                }
                finally
                {
                    await restartedHost.StopAsync(cancellationToken);
                }
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Lifecycle_state_and_leaderboard_dirty_roll_back_then_commit_together_on_replay(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            var competitionId = Guid.CreateVersion7();
            using var host = BuildHost(connectionString);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.EnsureCreatedAsync(cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var ownerId = Guid.CreateVersion7();
                db.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "lifecycle-owner",
                    NormalizedUserName = "LIFECYCLE-OWNER",
                    Email = "lifecycle-owner@example.test",
                    PasswordHash = "test",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                db.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    Title = "Lifecycle outbox",
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Running,
                    ConfigurationJson = "{}",
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    FlagDerivationSecret = new byte[32],
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            LifecycleTransitionProbeHandler.Fail = true;
            await host.StartAsync(cancellationToken);
            try
            {
                await host.Services.GetRequiredService<IMessageBus>()
                    .SendAsync(new FinishLifecycleProbe(competitionId));
                var deadLetters = host.Services.GetRequiredService<IWolverineRuntime>()
                    .Storage.DeadLetters;
                var deadLetter = await WaitForLifecycleDeadLetterAsync(
                    deadLetters,
                    cancellationToken);
                await AssertLifecycleStatusAsync(
                    host,
                    competitionId,
                    CompetitionStatus.Running,
                    cancellationToken);

                LifecycleTransitionProbeHandler.Fail = false;
                await deadLetters.ReplayAsync(
                    new DeadLetterEnvelopeQuery([deadLetter.Id]),
                    cancellationToken);

                await WaitForLifecycleStatusAsync(
                    host,
                    competitionId,
                    CompetitionStatus.Finished,
                    cancellationToken);
                await AssertLifecycleStatusAsync(
                    host,
                    competitionId,
                    CompetitionStatus.Finished,
                    cancellationToken);
            }
            finally
            {
                LifecycleTransitionProbeHandler.Fail = true;
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awd_round_fact_and_node_injection_share_real_wolverine_durability(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            using var host = BuildHost(
                postgres.GetConnectionString(),
                timeProvider: new FakeTimeProvider(now));
            Guid competitionId;
            Guid competitionChallengeId;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.EnsureCreatedAsync(cancellationToken);
                var ownerId = Guid.CreateVersion7();
                competitionId = Guid.CreateVersion7();
                var challengeId = Guid.CreateVersion7();
                competitionChallengeId = Guid.CreateVersion7();
                var teamId = Guid.CreateVersion7();
                db.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "awd-outbox-owner",
                    NormalizedUserName = "AWD-OUTBOX-OWNER",
                    Email = "awd-outbox@example.test",
                    PasswordHash = "test",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                db.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    Title = "AWD outbox",
                    OwnerId = ownerId,
                    Mode = GameMode.Awd,
                    Status = CompetitionStatus.Running,
                    ConfigurationJson = System.Text.Json.JsonSerializer.Serialize(
                        AwdConfiguration.Default with
                        {
                            HardeningDurationSeconds = 0,
                            RoundDurationSeconds = 300
                        },
                        new System.Text.Json.JsonSerializerOptions(
                            System.Text.Json.JsonSerializerDefaults.Web)),
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                db.Challenges.Add(new Challenge
                {
                    Id = challengeId,
                    OwnerId = ownerId,
                    Mode = GameMode.Awd,
                    Title = "AWD outbox challenge",
                    DefinitionJson = System.Text.Json.JsonSerializer.Serialize(
                        new AwdChallengeConfiguration(
                            AwdChallengeConfiguration.CurrentSchemaVersion),
                        new System.Text.Json.JsonSerializerOptions(
                            System.Text.Json.JsonSerializerDefaults.Web)),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                db.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    IsPublished = true,
                    RulesJson = System.Text.Json.JsonSerializer.Serialize(
                        new AwdChallengeConfiguration(
                            AwdChallengeConfiguration.CurrentSchemaVersion),
                        new System.Text.Json.JsonSerializerOptions(
                            System.Text.Json.JsonSerializerDefaults.Web)),
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
                db.RuntimeInstances.Add(new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = RuntimeProvider.Docker,
                    RunnerId = "test-runner",
                    State = RuntimeState.Running,
                    ProviderReceiptJson = "{}",
                    CreatedAt = now,
                    RunningAt = now
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            var observed = AwdInjectionObservation.Expect();
            await host.StartAsync(cancellationToken);
            try
            {
                var message = new AdvanceAwdRound(
                    competitionId,
                    competitionChallengeId,
                    now);
                var bus = host.Services.GetRequiredService<IMessageBus>();
                await bus.SendAsync(message);
                await Assert.That(await observed.WaitAsync(
                    TimeSpan.FromSeconds(30),
                    cancellationToken)).IsTrue();
                await Task.Delay(250, cancellationToken);
                await using var scope = host.Services.CreateAsyncScope();
                var flagCount = await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>()
                    .ChallengeFlags.CountAsync(
                        flag => flag.CompetitionChallengeId == competitionChallengeId,
                        cancellationToken);
                await Assert.That(flagCount).IsEqualTo(1);
                await Assert.That(AwdInjectionObservation.Count).IsEqualTo(1);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    private static PostgreSqlContainer CreatePostgres() =>
        new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("noctf_wolverine_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static async Task<DeadLetterEnvelope> WaitForDeadLetterAsync(
        IDeadLetters deadLetters,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var result = await deadLetters.QueryAsync(
                new DeadLetterEnvelopeQuery
                {
                    PageSize = 1,
                    MessageType = typeof(WriteRollbackOutboxProbe).FullName
                },
                cancellationToken);
            var envelope = result.Envelopes.FirstOrDefault();
            if (envelope is not null)
                return envelope;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        throw new TimeoutException("The rollback probe did not reach Wolverine dead-letter storage.");
    }

    private static async Task<DeadLetterEnvelope> WaitForLifecycleDeadLetterAsync(
        IDeadLetters deadLetters,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var result = await deadLetters.QueryAsync(
                new DeadLetterEnvelopeQuery
                {
                    PageSize = 1,
                    MessageType = typeof(FinishLifecycleProbe).FullName
                },
                cancellationToken);
            var envelope = result.Envelopes.FirstOrDefault();
            if (envelope is not null)
                return envelope;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        throw new TimeoutException("The lifecycle probe did not reach Wolverine dead-letter storage.");
    }

    private static async Task AssertLifecycleStatusAsync(
        IHost host,
        Guid competitionId,
        CompetitionStatus expected,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var status = await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>()
            .Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.Status)
            .SingleAsync(cancellationToken);
        await Assert.That(status).IsEqualTo(expected);
    }

    private static async Task WaitForLifecycleStatusAsync(
        IHost host,
        Guid competitionId,
        CompetitionStatus expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            var status = await db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == competitionId)
                .Select(competition => competition.Status)
                .SingleAsync(cancellationToken);
            if (status == expected)
                return;
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException($"Competition {competitionId} did not reach {expected}.");
    }

    private static async Task WaitForGameplayFactStateAsync(
        IHost host,
        Guid gameplayFactId,
        GameplayFactState expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            var state = await db.GameplayFacts.AsNoTracking()
                .Where(item => item.Id == gameplayFactId)
                .Select(item => item.State)
                .SingleAsync(cancellationToken);
            if (state == expected)
                return;
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException(
            $"Gameplay fact {gameplayFactId} did not reach {expected}.");
    }

    private static async Task SeedAwdpFixAsync(
        IHost host,
        AwdpFixExecutionFixture fixture,
        CancellationToken cancellationToken)
    {
        var now = fixture.Now;
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "awdp-result-owner",
            NormalizedUserName = "AWDP-RESULT-OWNER",
            Email = "awdp-result@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP result outbox",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Title = "AWDP result target",
            Visibility = ChallengeVisibility.Private,
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = fixture.CompetitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 100,
            IsPublished = true,
            RulesJson = "{}",
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "awdp-result-team",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = fixture.GameplayFactId,
            CompetitionId = competitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = teamId,
            ActorUserId = ownerId,
            Kind = GameplayFactKind.FixAttempt,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = fixture.PatchUploadId,
            State = GameplayFactState.Processing,
            OccurredAt = now,
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = fixture.RuntimeInstanceId,
            CompetitionId = competitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.AwdpTarget,
            GameplayFactId = fixture.GameplayFactId,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerId = "test-runner",
            State = RuntimeState.Running,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now,
            ExpiresAt = now.AddMinutes(5)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IHost BuildHost(
        string connectionString,
        string envelopeSchema = "wolverine_test",
        AwdpFixExecutionFixture? awdpFixExecution = null,
        TimeProvider? timeProvider = null)
    {
        var builder = Host.CreateApplicationBuilder();
        if (awdpFixExecution is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runner:Pool"] = "test-pool",
                ["Runner:Id"] = awdpFixExecution.Message.RunnerId
            });
            builder.Services.AddSingleton<IAwdpFixWorkReader>(awdpFixExecution.WorkReader);
            builder.Services.AddOptions<RunnerOptions>()
                .Bind(builder.Configuration.GetSection(RunnerOptions.SectionName));
            builder.Services.AddSingleton<IRuntimeProviderCatalog>(awdpFixExecution.Providers);
            builder.Services.AddSingleton<IOneShotRuntimeProviderCatalog>(
                awdpFixExecution.OneShotProviders);
            builder.Services.AddSingleton<IAwdpCheckerExecutor, AwdpCheckerExecutor>();
            builder.Services.AddSingleton(new AwdpFixArchiveDownloader(
                awdpFixExecution.HttpClientFactory));
            builder.Services.AddSingleton<FixArchivePreparer>();
            builder.Services.AddSingleton(Substitute.For<IRunnerCapacityGate>());
        }
        builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        builder.Services.AddScoped<ICompetitionEventRecorder, CompetitionEventStore>();
        builder.Services.AddScoped<IInternalResultStore, InternalResultStore>();
        builder.Services.AddScoped<ICompetitionLifecycleStore, EmptyLifecycleStore>();
        builder.Services.AddScoped<LifecycleAdvancer>();
        builder.Services.AddScoped<IAwdRoundCoordinator, PostgresAwdRoundCoordinator>();
        builder.Services.AddSingleton<IAwdRoundConfigurationCatalog, AwdRoundConfigurationCatalog>();
        builder.Services.AddSingleton<KohProducerConfigurationCatalog>();
        builder.Services.AddSingleton<IKohControlClient, UnusedKohControlClient>();
        builder.Services.AddSingleton(timeProvider ?? TimeProvider.System);
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<OutboxBusinessProbeHandler>();
            options.Discovery.IncludeType<ObserveOutboxBusinessProbeHandler>();
            options.Discovery.IncludeType<ScheduledOutboxProbeHandler>();
            options.Discovery.IncludeType<ObserveScheduledOutboxProbeHandler>();
            options.Discovery.IncludeType<RollbackOutboxProbeHandler>();
            options.Discovery.IncludeType<ObserveRollbackOutboxProbeHandler>();
            options.Discovery.IncludeType<LifecycleMaintenanceProbeHandler>();
            options.Discovery.IncludeType<LifecycleTransitionProbeHandler>();
            options.Discovery.IncludeType<ObserveLifecycleProjectionHandler>();
            options.Discovery.IncludeType<AwdRoundProbeHandler>();
            options.Discovery.IncludeType<ObserveAwdInjectionHandler>();
            options.Discovery.IncludeType<KohPollingHandler>();
            options.Discovery.IncludeType<KohObservationHandler>();
            options.Discovery.IncludeType<AwdpReplayProbeHandler>();
            options.Discovery.IncludeType<AwdpFixResultProbeHandler>();
            if (awdpFixExecution is not null)
                options.Discovery.IncludeType<AwdpFixVerificationHandler>();
            options.PersistMessagesWithPostgresql(connectionString, envelopeSchema);
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.All;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
            options.Policies.OnException<RollbackProbeException>().MoveToErrorQueue();
            options.Policies.OnException<DbUpdateConcurrencyException>().RetryTimes(5);
            options.Policies.OnException<LifecycleTransitionProbeException>().MoveToErrorQueue();
            options.Policies.OnException<AwdpReplayProbeException>().RetryTimes(2);
            options.ListenToPostgresqlQueue("outbox-probe").UseDurableInbox();
            options.ListenToPostgresqlQueue(
                NoCTF.Application.Runtime.Instances.RunnerNodeQueueName
                    .FromRunnerId("test-runner").Value)
                .UseDurableInbox();
            options.PublishMessage<WriteOutboxBusinessProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveOutboxBusinessProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<WriteScheduledOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveScheduledOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<WriteRollbackOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveRollbackOutboxProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<LifecycleMessage>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<FinishLifecycleProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ProjectLeaderboard>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<AdvanceAwdRound>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<GenerateAwdFlags>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<CompleteAwdpFixRecovery>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<AwdpFixResult>()
                .ToPostgresqlQueue("outbox-probe");
            if (awdpFixExecution is not null)
                options.PublishMessage<RunAwdpFixVerification>()
                    .ToPostgresqlQueue(
                        NoCTF.Application.Runtime.Instances.RunnerNodeQueueName
                            .FromRunnerId("test-runner").Value);
        });
        return builder.Build();
    }

    private static IHost BuildMaintenanceHost(string connectionString, string hostId)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new MaintenanceHostIdentity(hostId));
        builder.Services.AddSingleton(new ClusterSchedulerNodeIdentity(hostId));
        builder.Services.AddSingleton<ClusterSchedulingState>();
        builder.Services.AddSingleton<LeaderboardProjectionMergeQueue>();
        builder.Services.AddSingleton<IClusterSchedulerStatusStore, InMemorySchedulerStatusStore>();
        builder.Services.AddScoped<IClusterScheduleSource, EmptyClusterScheduleSource>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingularAgent<MaintenanceTickAgent>();
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<MaintenanceTickProbeHandler>();
            options.PersistMessagesWithPostgresql(connectionString, "wolverine_maintenance_test");
            options.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.All;
            options.ListenToPostgresqlQueue("maintenance-projection-test")
                .UseDurableInbox();
            options.PublishMessage<ProjectLeaderboard>()
                .ToPostgresqlQueue("maintenance-projection-test");
            options.Durability.CheckAssignmentPeriod = TimeSpan.FromMilliseconds(250);
            options.Durability.FirstHealthCheckExecution = TimeSpan.FromMilliseconds(100);
            options.Durability.HealthCheckPollingTime = TimeSpan.FromMilliseconds(250);
            options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(1);
        });
        return builder.Build();
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        throw new TimeoutException("The Wolverine maintenance agent did not reach the expected state.");
    }

    private sealed class AwdpFixExecutionFixture
    {
        private AwdpFixExecutionFixture(
            DateTimeOffset now,
            Guid gameplayFactId,
            Guid competitionChallengeId,
            Guid patchUploadId,
            Guid runtimeInstanceId,
            RunAwdpFixVerification message,
            IAwdpFixWorkReader workReader,
            IRuntimeProviderCatalog providers,
            IOneShotRuntimeProviderCatalog oneShotProviders,
            ProbeOneShotRunner oneShotRunner,
            IHttpClientFactory httpClientFactory)
        {
            Now = now;
            GameplayFactId = gameplayFactId;
            CompetitionChallengeId = competitionChallengeId;
            PatchUploadId = patchUploadId;
            RuntimeInstanceId = runtimeInstanceId;
            Message = message;
            WorkReader = workReader;
            Providers = providers;
            OneShotProviders = oneShotProviders;
            OneShotRunner = oneShotRunner;
            HttpClientFactory = httpClientFactory;
        }

        public DateTimeOffset Now { get; }
        public Guid GameplayFactId { get; }
        public Guid CompetitionChallengeId { get; }
        public Guid PatchUploadId { get; }
        public Guid RuntimeInstanceId { get; }
        public RunAwdpFixVerification Message { get; }
        public IAwdpFixWorkReader WorkReader { get; }
        public IRuntimeProviderCatalog Providers { get; }
        public IOneShotRuntimeProviderCatalog OneShotProviders { get; }
        public ProbeOneShotRunner OneShotRunner { get; }
        public IHttpClientFactory HttpClientFactory { get; }

        public static AwdpFixExecutionFixture Create(int exitCode)
        {
            var now = DateTimeOffset.UtcNow;
            var gameplayFactId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var patchUploadId = Guid.CreateVersion7();
            var runtimeInstanceId = Guid.CreateVersion7();
            var message = new RunAwdpFixVerification(
                gameplayFactId,
                competitionChallengeId,
                patchUploadId,
                runtimeInstanceId,
                now.AddMinutes(5),
                "test-runner");
            var archiveBytes = CreateFixArchive();
            var work = new AwdpFixWork(
                new(
                    new Uri("https://api.example.test/fix-archive"),
                    "archive-token",
                    "fix.tar.gz",
                    archiveBytes.LongLength,
                    SHA256.HashData(archiveBytes)),
                new(
                    runtimeInstanceId,
                    RuntimeProvider.Docker,
                    "awdp-target",
                    RuntimeStatus.Running,
                    new Dictionary<int, int>(),
                    null,
                    "awdp-target",
                    "awdp-network",
                    runtimeInstanceId),
                "fix.sh",
                ["/bin/sh", "/noctf/fix/fix.sh"],
                TimeSpan.FromMinutes(1),
                new(
                    runtimeInstanceId,
                    RuntimeProvider.Docker,
                    "checker:test",
                    [],
                    new Dictionary<string, string>(),
                    "awdp-network",
                    "awdp-target",
                    5,
                    new Uri("https://api.example.test/fix-result"),
                    "callback-token",
                    TimeSpan.FromMinutes(1)));
            var reader = Substitute.For<IAwdpFixWorkReader>();
            reader.ClaimAsync(message, Arg.Any<CancellationToken>())
                .Returns(new AwdpFixWorkClaim(
                    AwdpFixExecutionFenceDisposition.Execute,
                    work));
            var sandbox = Substitute.For<IContainerSandboxLifecycle>();
            sandbox.CopyArchiveAsync(
                    Arg.Any<ContainerReceipt>(),
                    Arg.Any<Stream>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            sandbox.ExecAsync(
                    Arg.Any<ContainerReceipt>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ContainerExecResult(0, false));
            var providers = Substitute.For<IRuntimeProviderCatalog>();
            providers.Sandbox(RuntimeProvider.Docker).Returns(sandbox);
            var oneShotRunner = new ProbeOneShotRunner(exitCode);
            var oneShotProviders = Substitute.For<IOneShotRuntimeProviderCatalog>();
            oneShotProviders.OneShot(RuntimeProvider.Docker).Returns(oneShotRunner);
            var httpClientFactory = new StaticHttpClientFactory(
                new HttpClient(new StaticContentHandler(archiveBytes)));
            return new(
                now,
                gameplayFactId,
                competitionChallengeId,
                patchUploadId,
                runtimeInstanceId,
                message,
                reader,
                providers,
                oneShotProviders,
                oneShotRunner,
                httpClientFactory);
        }

        private static byte[] CreateFixArchive()
        {
            using var archive = new MemoryStream();
            using (var gzip = new GZipStream(
                       archive,
                       CompressionMode.Compress,
                       leaveOpen: true))
            using (var tar = new TarWriter(gzip, leaveOpen: true))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
                });
            }
            return archive.ToArray();
        }
    }

    private sealed class ProbeOneShotRunner(int exitCode) : IOneShotJobRunner
    {
        private int executionCount;

        public int ExecutionCount => Volatile.Read(ref executionCount);

        public Task<OneShotResult> RunAsync(
            ContainerRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executionCount);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new OneShotResult(
                "awdp-checker",
                exitCode,
                string.Empty,
                "checker failed",
                now,
                now));
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
    }

    public sealed class EmptyLifecycleStore : ICompetitionLifecycleStore
    {
        public Task<CompetitionStatus?> GetStatusAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<CompetitionStatus?>(null);

        public Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompetitionLifecycleSnapshot>>([]);

        public Task<bool> TryTransitionAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    public sealed class UnusedKohControlClient : IKohControlClient
    {
        public Task<KohControlResponse> ObserveAsync(
            Uri controlUrl,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Wolverine code-generation probe does not poll KoH.");
    }
}

public sealed record WriteOutboxBusinessProbe(Guid Id);
public sealed record ObserveOutboxBusinessProbe(Guid Id);
public sealed record WriteScheduledOutboxProbe(Guid Id, DateTimeOffset DueAt);
public sealed record ObserveScheduledOutboxProbe(Guid Id);
public sealed record WriteRollbackOutboxProbe(Guid Id);
public sealed record ObserveRollbackOutboxProbe(Guid Id);
public sealed record FinishLifecycleProbe(Guid CompetitionId);

public static class MaintenanceTickObservation
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int>
        DispatchCounts = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int>
        LeaderboardDispatchCounts = new();

    public static int ActiveHostCount => DispatchCounts.Count(item => item.Value > 0);

    public static IReadOnlyList<string> ActiveHostIds => DispatchCounts
        .Where(item => item.Value > 0)
        .Select(item => item.Key)
        .ToArray();

    public static int DispatchCount(string hostId) =>
        DispatchCounts.GetValueOrDefault(hostId);

    public static void Reset()
    {
        DispatchCounts.Clear();
        LeaderboardDispatchCounts.Clear();
    }

    public static void RecordDispatch(string hostId) =>
        DispatchCounts.AddOrUpdate(hostId, 1, (_, count) => count + 1);

    public static void RecordLeaderboardDispatch(Guid competitionId) =>
        LeaderboardDispatchCounts.AddOrUpdate(competitionId, 1, (_, count) => count + 1);

    public static int LeaderboardDispatchCount(Guid competitionId) =>
        LeaderboardDispatchCounts.GetValueOrDefault(competitionId);
}

public sealed record MaintenanceHostIdentity(string Value);

public sealed class EmptyClusterScheduleSource : IClusterScheduleSource
{
    public Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClusterScheduleEntry>>([]);
}

public sealed class InMemorySchedulerStatusStore : IClusterSchedulerStatusStore
{
    private ClusterSchedulerStatus? scheduler;

    public Task TakeOverAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken)
    {
        scheduler = status;
        return Task.CompletedTask;
    }

    public Task RenewAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken)
    {
        if (scheduler != status)
            throw new InvalidOperationException("Scheduler ownership changed.");
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken)
    {
        if (scheduler == status)
            scheduler = null;
        return Task.CompletedTask;
    }

    public Task<ClusterSchedulerStatus?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(scheduler);
}

public sealed class MaintenanceTickProbeHandler
{
    public static void Handle(DispatchAwdCheckers _, MaintenanceHostIdentity host) =>
        MaintenanceTickObservation.RecordDispatch(host.Value);

    public static void Handle(ReconcileRunnerAssignments _) { }

    public static void Handle(LifecycleMessage _) { }

    public static void Handle(ProjectLeaderboard message) =>
        MaintenanceTickObservation.RecordLeaderboardDispatch(message.CompetitionId);
}

public sealed class OutboxBusinessProbeHandler
{
    public static async Task Handle(
        WriteOutboxBusinessProbe message,
        NoCtfDbContext db,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO outbox_business_probe (id) VALUES ({message.Id})",
            cancellationToken);
        await outbox.PublishAsync(new ObserveOutboxBusinessProbe(message.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}

public sealed class ScheduledOutboxProbeHandler
{
    public static async Task Handle(
        WriteScheduledOutboxProbe message,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await outbox.ScheduleAsync(new ObserveScheduledOutboxProbe(message.Id), message.DueAt);
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}

public sealed class ObserveScheduledOutboxProbeHandler
{
    public static void Handle(ObserveScheduledOutboxProbe message) =>
        ScheduledProbeObservation.Complete(message.Id, DateTimeOffset.UtcNow);
}

public sealed class RollbackOutboxProbeHandler
{
    public static volatile bool Fail = true;

    public static async Task Handle(
        WriteRollbackOutboxProbe message,
        NoCtfDbContext db,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO rollback_business_probe (id) VALUES ({message.Id})",
            cancellationToken);
        await outbox.PublishAsync(new ObserveRollbackOutboxProbe(message.Id));
        await db.SaveChangesAsync(cancellationToken);
        if (Fail)
            throw new RollbackProbeException();
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }
}

public sealed class ObserveRollbackOutboxProbeHandler
{
    public static void Handle(ObserveRollbackOutboxProbe message) =>
        RollbackProbeObservation.Complete(message.Id, true);
}

public sealed class RollbackProbeException : Exception;

public sealed class ObserveOutboxBusinessProbeHandler
{
    public static async Task Handle(
        ObserveOutboxBusinessProbe message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM outbox_business_probe WHERE id = {message.Id}")
            .SingleAsync(cancellationToken);
        OutboxProbeObservation.Complete(message.Id, count == 1);
    }
}

public sealed class AwdpReplayProbeHandler
{
    public static void Handle(CompleteAwdpFixRecovery message)
    {
        if (AwdpReplayProbeObservation.RecordAttempt(message.GameplayFactId) == 1)
            throw new AwdpReplayProbeException();
        AwdpReplayProbeObservation.Complete(message.GameplayFactId, message);
    }
}

public sealed class AwdpFixResultProbeHandler
{
    public static async Task Handle(
        AwdpFixResult message,
        IInternalResultStore results,
        CancellationToken cancellationToken)
    {
        var disposition = await results.RecordAwdpAsync(message, cancellationToken);
        AwdpFixResultObservation.Complete(message, disposition);
    }

    public static void Handle(StopContainerRuntime _) { }
}

public sealed record AwdpFixResultRecording(
    AwdpFixResult Message,
    InternalResultDisposition Disposition);

public static class AwdpFixResultObservation
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int>
        DeliveryCounts = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,
        TaskCompletionSource<AwdpFixResultRecording>> Observations = new();

    public static TaskCompletionSource<AwdpFixResultRecording> Expect(Guid gameplayFactId)
    {
        DeliveryCounts.TryRemove(gameplayFactId, out _);
        var completion = new TaskCompletionSource<AwdpFixResultRecording>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Observations[gameplayFactId] = completion;
        return completion;
    }

    public static int Count(Guid gameplayFactId) =>
        DeliveryCounts.GetValueOrDefault(gameplayFactId);

    public static void Complete(
        AwdpFixResult message,
        InternalResultDisposition disposition)
    {
        DeliveryCounts.AddOrUpdate(message.GameplayFactId, 1, (_, count) => count + 1);
        if (Observations.TryRemove(message.GameplayFactId, out var completion))
            completion.TrySetResult(new(message, disposition));
    }
}

public sealed class AwdpReplayProbeException : Exception;

public static class AwdpReplayProbeObservation
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int>
        DeliveryAttempts = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,
        TaskCompletionSource<CompleteAwdpFixRecovery>> Observations = new();

    public static TaskCompletionSource<CompleteAwdpFixRecovery> Expect(Guid id)
    {
        DeliveryAttempts.TryRemove(id, out _);
        var completion = new TaskCompletionSource<CompleteAwdpFixRecovery>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Observations[id] = completion;
        return completion;
    }

    public static int RecordAttempt(Guid id) =>
        DeliveryAttempts.AddOrUpdate(id, 1, (_, count) => count + 1);

    public static int Attempts(Guid id) => DeliveryAttempts.GetValueOrDefault(id);

    public static void Complete(Guid id, CompleteAwdpFixRecovery message)
    {
        if (Observations.TryRemove(id, out var completion))
            completion.TrySetResult(message);
    }
}

public sealed class LifecycleMaintenanceProbeHandler
{
    public static async Task Handle(
        LifecycleMessage message,
        LifecycleAdvancer advancer,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var outcome = await BackendMessageHandlers.ExecuteCompetitionLifecycleAsync(
            message,
            advancer,
            db,
            outbox,
            cancellationToken);
        LifecycleTickObservation.Record(outcome);
    }
}

public sealed class LifecycleTransitionProbeHandler
{
    public static volatile bool Fail = true;

    public static async Task Handle(
        FinishLifecycleProbe message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var store = new CompetitionLifecycleStore(db, null!, outbox);
        var applied = await store.TryTransitionWithAuditAsync(
            message.CompetitionId,
            CompetitionStatus.Running,
            CompetitionStatus.Finished,
            null,
            "integration_probe",
            true,
            CompetitionLifecycleEffects.None,
            cancellationToken);
        if (!applied)
            throw new InvalidOperationException("The lifecycle transition was not applied.");
        if (Fail)
            throw new LifecycleTransitionProbeException();
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }
}

public sealed class ObserveLifecycleProjectionHandler
{
    public static async Task Handle(
        ProjectLeaderboard message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var committed = await db.Competitions.AsNoTracking().AnyAsync(
            competition => competition.Id == message.CompetitionId
                && competition.Status == CompetitionStatus.Finished,
            cancellationToken);
        LifecycleTransitionObservation.Complete(message.CompetitionId, committed);
    }
}

public sealed class LifecycleTransitionProbeException : Exception;

public sealed class AwdRoundProbeHandler
{
    public static async Task Handle(
        AdvanceAwdRound message,
        IAwdRoundCoordinator coordinator,
        CancellationToken cancellationToken) =>
        _ = await coordinator.AdvanceAsync(message, cancellationToken);

    public static async Task Handle(
        GenerateAwdFlags message,
        IAwdRoundCoordinator coordinator,
        CancellationToken cancellationToken) =>
        _ = await coordinator.GenerateFlagsAsync(message, cancellationToken);
}

public sealed class ObserveAwdInjectionHandler
{
    public static async Task Handle(
        InjectAwdFlag message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var committed = await db.ChallengeFlags.AsNoTracking().AnyAsync(
            flag => flag.Id == message.ChallengeFlagId,
            cancellationToken);
        AwdInjectionObservation.Record(committed);
    }
}

internal static class AwdInjectionObservation
{
    private static TaskCompletionSource<bool> completion = CreateSource();
    private static int count;

    public static int Count => Volatile.Read(ref count);

    public static Task<bool> Expect()
    {
        Interlocked.Exchange(ref count, 0);
        completion = CreateSource();
        return completion.Task;
    }

    public static void Record(bool committed)
    {
        Interlocked.Increment(ref count);
        completion.TrySetResult(committed);
    }

    private static TaskCompletionSource<bool> CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record LifecycleTickTasks(
    Task Completed,
    IReadOnlyList<MessageExecutionOutcome> Outcomes);

internal static class LifecycleTickObservation
{
    private static readonly object Sync = new();
    private static TaskCompletionSource completion = CreateSource();
    private static readonly List<MessageExecutionOutcome> Outcomes = [];
    private static int expectedCount;

    public static LifecycleTickTasks Expect(int expected)
    {
        lock (Sync)
        {
            completion = CreateSource();
            expectedCount = expected;
            Outcomes.Clear();
            return new(completion.Task, Outcomes);
        }
    }

    public static void Record(MessageExecutionOutcome outcome)
    {
        lock (Sync)
        {
            Outcomes.Add(outcome);
            if (Outcomes.Count >= expectedCount)
                completion.TrySetResult();
        }
    }

    private static TaskCompletionSource CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal static class LifecycleTransitionObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<bool>> Pending = [];

    public static Task<bool> Expect(Guid competitionId)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(competitionId, source);
            return source.Task;
        }
    }

    public static void Complete(Guid competitionId, bool committed)
    {
        TaskCompletionSource<bool>? source;
        lock (Sync)
        {
            if (!Pending.Remove(competitionId, out source))
                return;
        }
        source.SetResult(committed);
    }
}

internal static class OutboxProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<bool>> Pending = [];

    public static Task<bool> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, bool factWasVisible)
    {
        TaskCompletionSource<bool>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(factWasVisible);
    }
}

internal static class ScheduledProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<DateTimeOffset>> Pending = [];

    public static Task<DateTimeOffset> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<DateTimeOffset>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, DateTimeOffset observedAt)
    {
        TaskCompletionSource<DateTimeOffset>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(observedAt);
    }
}

internal static class RollbackProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<bool>> Pending = [];

    public static Task<bool> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, bool applied)
    {
        TaskCompletionSource<bool>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(applied);
    }
}
