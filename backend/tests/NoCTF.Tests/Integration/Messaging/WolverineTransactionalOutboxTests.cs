using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
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
using NoCTF.GameModes.Koh.Configuration;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class WolverineTransactionalOutboxTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Consumer_observes_business_fact_committed_with_outbox(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
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
    public async Task Lifecycle_chain_survives_restart_and_replayed_version_has_one_successor(
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
                    .Database.MigrateAsync(cancellationToken);
            }

            var observation = LifecycleChainObservation.Expect();
            using (var firstHost = BuildHost(connectionString))
            {
                await firstHost.StartAsync(cancellationToken);
                try
                {
                    var bus = firstHost.Services.GetRequiredService<IMessageBus>();
                    var now = DateTimeOffset.UtcNow;
                    await bus.SendAsync(new LifecycleMessage(now, 1));
                    await bus.SendAsync(new LifecycleMessage(now, 1));
                    await observation.FirstCommitted.WaitAsync(
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                    await WaitForLifecycleVersionAsync(firstHost, 2, cancellationToken);
                }
                finally
                {
                    await firstHost.StopAsync(cancellationToken);
                }
            }

            using (var restartedHost = BuildHost(connectionString))
            {
                await restartedHost.StartAsync(cancellationToken);
                try
                {
                    await observation.SuccessorCommitted.WaitAsync(
                        TimeSpan.FromSeconds(45),
                        cancellationToken);
                    await using var scope = restartedHost.Services.CreateAsyncScope();
                    var schedule = await scope.ServiceProvider
                        .GetRequiredService<NoCtfDbContext>()
                        .DurableMaintenanceSchedules.AsNoTracking()
                        .SingleAsync(
                            candidate => candidate.Kind
                                == MaintenanceChainKind.CompetitionLifecycle,
                            cancellationToken);
                    await Assert.That(schedule.ProcessingVersion).IsEqualTo(3);
                    await Assert.That(observation.AppliedVersions.Count(version => version == 1))
                        .IsEqualTo(1);
                    await Assert.That(observation.AppliedVersions.Count(version => version == 2))
                        .IsEqualTo(1);
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
    public async Task Lifecycle_state_and_outbox_roll_back_then_commit_together_on_replay(
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
                await db.Database.MigrateAsync(cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var ownerId = Guid.CreateVersion7();
                db.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "lifecycle-owner",
                    NormalizedUserName = "LIFECYCLE-OWNER",
                    Email = "lifecycle-owner@example.test",
                    NormalizedEmail = "LIFECYCLE-OWNER@EXAMPLE.TEST",
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
                    RunningSince = now.AddMinutes(-1),
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    FlagDerivationSecret = new byte[32],
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConfigurationUpdatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            LifecycleTransitionProbeHandler.Fail = true;
            var observed = LifecycleTransitionObservation.Expect(competitionId);
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

                await Assert.That(await observed.WaitAsync(cancellationToken)).IsTrue();
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
    public async Task Awd_round_fact_node_injection_and_replay_share_real_wolverine_durability(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            using var host = BuildHost(postgres.GetConnectionString());
            var now = DateTimeOffset.UtcNow;
            Guid competitionId;
            Guid competitionChallengeId;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
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
                    NormalizedEmail = "AWD-OUTBOX@EXAMPLE.TEST",
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
                    RunningSince = now.AddMinutes(-1),
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
                    NormalizedName = "BLUE",
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
                    Generation = 1,
                    RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = RuntimeProvider.Docker,
                    RunnerPool = "test-pool",
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
                    now,
                    0,
                    0);
                var bus = host.Services.GetRequiredService<IMessageBus>();
                await bus.SendAsync(message);
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
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_wolverine_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static async Task WaitForLifecycleVersionAsync(
        IHost host,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var version = await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>()
                .DurableMaintenanceSchedules.AsNoTracking()
                .Where(candidate => candidate.Kind == MaintenanceChainKind.CompetitionLifecycle)
                .Select(candidate => candidate.ProcessingVersion)
                .SingleAsync(cancellationToken);
            if (version >= expectedVersion)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        throw new TimeoutException(
            $"The lifecycle chain did not commit processing version {expectedVersion}.");
    }

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

    private static IHost BuildHost(
        string connectionString,
        string envelopeSchema = "wolverine_test")
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        builder.Services.AddScoped<ICompetitionLifecycleStore, EmptyLifecycleStore>();
        builder.Services.AddScoped<LifecycleAdvancer>();
        builder.Services.AddScoped<IAwdRoundCoordinator, PostgresAwdRoundCoordinator>();
        builder.Services.AddSingleton<AwdRoundConfigurationCatalog>();
        builder.Services.AddSingleton<KohProducerConfigurationCatalog>();
        builder.Services.AddSingleton<IKohControlClient, UnusedKohControlClient>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.UseWolverine(options =>
        {
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
            options.PersistMessagesWithPostgresql(connectionString, envelopeSchema);
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.All;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
            options.Policies.OnException<RollbackProbeException>().MoveToErrorQueue();
            options.Policies.OnException<DbUpdateConcurrencyException>().RetryTimes(5);
            options.Policies.OnException<LifecycleTransitionProbeException>().MoveToErrorQueue();
            options.ListenToPostgresqlQueue("outbox-probe").UseDurableInbox();
            options.ListenToPostgresqlQueue(
                NoCTF.Application.Runtime.Instances.RunnerNodeQueueName
                    .FromAssignment("test-pool", "test-runner").Value)
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
        });
        return builder.Build();
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
        LifecycleChainObservation.Record(message.ProcessingVersion, outcome);
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
            CompetitionLifecycleEffects.ProjectLeaderboard,
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

internal sealed record LifecycleChainTasks(
    Task FirstCommitted,
    Task SuccessorCommitted,
    IReadOnlyList<long> AppliedVersions);

internal static class LifecycleChainObservation
{
    private static readonly object Sync = new();
    private static TaskCompletionSource First = CreateSource();
    private static TaskCompletionSource Successor = CreateSource();
    private static readonly List<long> Versions = [];

    public static LifecycleChainTasks Expect()
    {
        lock (Sync)
        {
            First = CreateSource();
            Successor = CreateSource();
            Versions.Clear();
            return new(First.Task, Successor.Task, Versions);
        }
    }

    public static void Record(long version, MessageExecutionOutcome outcome)
    {
        if (outcome is not (MessageExecutionOutcome.Applied or MessageExecutionOutcome.Idempotent))
            return;
        lock (Sync)
        {
            Versions.Add(version);
            if (version == 1)
                First.TrySetResult();
            else if (version == 2)
                Successor.TrySetResult();
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
