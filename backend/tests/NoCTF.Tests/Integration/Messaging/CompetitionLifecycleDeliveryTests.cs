using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("CompetitionLifecycle")]
[NotInParallel]
public sealed class CompetitionLifecycleDeliveryTests
{
    private const string NatsImage =
        "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d";
    private const string ConsumerName = "lifecycle-delivery-test";

    [Test]
    [Timeout(300_000)]
    public async Task Production_worker_processes_repeated_lifecycle_messages_without_dead_letters(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase($"noctf_lifecycle_delivery_{Guid.NewGuid():N}")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var nats = new ContainerBuilder(NatsImage)
                .WithPortBinding(4222, true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                nats.StartAsync(cancellationToken));

            var now = DateTimeOffset.UtcNow;
            var fixture = await SeedAsync(
                postgres.GetConnectionString(),
                now,
                cancellationToken);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString(),
                    ["ConnectionStrings:Nats"] =
                        $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}",
                    ["Wolverine:Nats:MaxDeliver"] = "2",
                    ["Wolverine:Nats:AckWaitSeconds"] = "5",
                    ["Wolverine:Nats:DuplicateWindowSeconds"] = "30"
                })
                .Build();
            var probe = new LifecycleSideEffectProbe();
            var queueProbe = new QueueServiceLocationProbe();
            var roles = HostRoles.Only(HostRole.Worker);
            using var host = Host.CreateDefaultBuilder()
                .UseEnvironment(Environments.Production)
                .ConfigureServices(services =>
                {
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton(probe);
                    services.AddSingleton<IQueueServiceLocationProbe>(_ => queueProbe);
                    services.AddDbContext<NoCtfDbContext>(options =>
                        options.UseNpgsql(postgres.GetConnectionString())
                            .UseSnakeCaseNamingConvention(),
                        contextLifetime: ServiceLifetime.Scoped,
                        optionsLifetime: ServiceLifetime.Singleton);
                    services.AddScoped<IPostCommitMessagePublisher,
                        WolverinePostCommitMessagePublisher>();
                    services.AddScoped<ICompetitionStartGateStore,
                        CompetitionStartGateStore>();
                    services.AddSingleton<ICompetitionConfigurationValidator,
                        GameModeCompetitionConfigurationValidator>();
                    services.AddSingleton<IChallengeConfigurationCatalog,
                        GameModeChallengeConfigurationCatalog>();
                    services.AddScoped<CompetitionStartGate>();
                    services.AddScoped<ICompetitionEventRecorder,
                        CompetitionEventStore>();
                    services.AddScoped<ICompetitionLifecycleStore,
                        CompetitionLifecycleStore>();
                    services.AddScoped<AdvanceCompetitionLifecycleUseCase>();
                })
                .UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(CompetitionLifecycleMessageHandler));
                    options.Discovery.IncludeType(typeof(LifecycleSideEffectHandler));
                    options.Discovery.IncludeType(typeof(QueueServiceLocationHandler));
                    options.ConfigureNoCtfPersistence(configuration, roles);
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
                    options.ListenToNatsSubject(NatsSubjects.Subject(WorkerQueue.Control))
                        .UseJetStream(NatsSubjects.ControlStream, ConsumerName)
                        .Named(ConsumerName);
                    foreach (var queue in new[]
                             {
                                 WorkerQueue.Gameplay,
                                 WorkerQueue.Projection,
                                 WorkerQueue.Background
                             })
                    {
                        options.ListenToNatsSubject(NatsSubjects.Subject(queue))
                            .UseJetStream(NatsSubjects.Stream(queue),
                                $"{ConsumerName}-{queue.ToString().ToLowerInvariant()}");
                    }
                    options.ListenToNatsSubject(NatsSubjects.Runner(ConsumerName))
                        .UseJetStream(NatsSubjects.RunnerStream,
                            $"{ConsumerName}-runner");
                    Route<AdvanceCompetitionLifecycle>(options);
                    Route<ProvisionCompetitionRuntimes>(options);
                    Route<CleanupCompetitionRuntimes>(options);
                    Route<ReconcileRunnerAssignments>(options);
                    Route<CompetitionEventCommitted>(options);
                    Route<ControlServiceLocationProbe>(options, WorkerQueue.Control);
                    Route<GameplayServiceLocationProbe>(options, WorkerQueue.Gameplay);
                    Route<ProjectionServiceLocationProbe>(options, WorkerQueue.Projection);
                    Route<BackgroundServiceLocationProbe>(options, WorkerQueue.Background);
                    options.PublishMessage<RunnerServiceLocationProbe>()
                        .ToNatsSubject(NatsSubjects.Runner(ConsumerName))
                        .UseJetStream(NatsSubjects.RunnerStream);
                })
                .Build();

            await host.StartAsync(cancellationToken);
            try
            {
                var bus = host.Services.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new AdvanceCompetitionLifecycle(now));
                await WaitForReconciliationAsync(probe, 1, cancellationToken);
                await AssertLifecycleAsync(
                    host.Services,
                    fixture,
                    expectedEvents: 2,
                    cancellationToken);

                await bus.PublishAsync(new ControlServiceLocationProbe());
                await bus.PublishAsync(new GameplayServiceLocationProbe());
                await bus.PublishAsync(new ProjectionServiceLocationProbe());
                await bus.PublishAsync(new BackgroundServiceLocationProbe());
                await bus.PublishAsync(new RunnerServiceLocationProbe());
                await WaitForQueueProbesAsync(queueProbe, cancellationToken);

                await bus.PublishAsync(new AdvanceCompetitionLifecycle(now.AddSeconds(1)));
                await WaitForReconciliationAsync(probe, 2, cancellationToken);
                await AssertLifecycleAsync(
                    host.Services,
                    fixture,
                    expectedEvents: 2,
                    cancellationToken);

            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    private static void Route<TMessage>(WolverineOptions options)
    {
        Route<TMessage>(options, WorkerQueue.Control);
    }

    private static void Route<TMessage>(WolverineOptions options, WorkerQueue queue)
    {
        options.PublishMessage<TMessage>()
            .ToNatsSubject(NatsSubjects.Subject(queue))
            .UseJetStream(NatsSubjects.Stream(queue));
    }

    private static async Task WaitForReconciliationAsync(
        LifecycleSideEffectProbe probe,
        int expected,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 80
            && Volatile.Read(ref probe.Reconciliations) < expected; attempt++)
        {
            await Task.Delay(250, cancellationToken);
        }
        await Assert.That(Volatile.Read(ref probe.Reconciliations))
            .IsGreaterThanOrEqualTo(expected);
    }

    private static async Task WaitForQueueProbesAsync(
        QueueServiceLocationProbe probe,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 80 && probe.Total < 5; attempt++)
            await Task.Delay(250, cancellationToken);
        await Assert.That(probe.Total).IsEqualTo(5);
    }

    private static async Task AssertLifecycleAsync(
        IServiceProvider services,
        LifecycleFixture fixture,
        int expectedEvents,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var statuses = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == fixture.PublishedId
                || competition.Id == fixture.RunningId)
            .ToDictionaryAsync(
                competition => competition.Id,
                competition => competition.Status,
                cancellationToken);
        await Assert.That(statuses[fixture.PublishedId])
            .IsEqualTo(CompetitionStatus.Running);
        await Assert.That(statuses[fixture.RunningId])
            .IsEqualTo(CompetitionStatus.Finished);
        await Assert.That(await db.CompetitionEvents.AsNoTracking()
                .CountAsync(@event => @event.Kind
                    == CompetitionEventKind.CompetitionLifecycleChanged,
                    cancellationToken))
            .IsEqualTo(expectedEvents);
    }

    private static async Task<LifecycleFixture> SeedAsync(
        string connectionString,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7(now.AddMinutes(-10));
        var challengeId = Guid.CreateVersion7(now.AddMinutes(-9));
        var competitionChallengeId = Guid.CreateVersion7(now.AddMinutes(-8));
        var teamId = Guid.CreateVersion7(now.AddMinutes(-7));
        var publishedId = Guid.CreateVersion7(now.AddMinutes(-6));
        var runningId = Guid.CreateVersion7(now.AddMinutes(-5));
        var configurations = new GameModeChallengeConfigurationCatalog();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "lifecycle-owner",
            NormalizedUserName = "LIFECYCLE-OWNER",
            Email = "lifecycle-owner@example.test",
            PasswordHash = "unused",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            AccountStatus = UserAccountStatus.Active,
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10)
        });
        db.Competitions.AddRange(
            Competition(publishedId, ownerId, CompetitionStatus.Published,
                now.AddMinutes(-1), now.AddHours(1), now),
            Competition(runningId, ownerId, CompetitionStatus.Running,
                now.AddHours(-1), now.AddMinutes(-1), now));
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Direction = "Pwn",
            Title = "Lifecycle delivery",
            Definition = configurations.CreateDefaultDefinitionForTest(GameMode.Ctf),
            CreatedAt = now.AddMinutes(-9),
            UpdatedAt = now.AddMinutes(-9)
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = publishedId,
            ChallengeId = challengeId,
            IsPublished = true,
            Rules = configurations.CreateDefaultRulesForTest(GameMode.Ctf),
            UpdatedAt = now.AddMinutes(-8)
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = publishedId,
            Name = "Lifecycle Team",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = new string('l', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now.AddMinutes(-7)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(publishedId, runningId);
    }

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        CompetitionStatus status,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now) => new CtfCompetition
    {
        Id = id,
        OwnerId = ownerId,
        Title = status + " lifecycle delivery",
        Status = status,
        ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
        StartAt = start,
        EndAt = end,
        FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
        CreatedAt = now.AddHours(-2),
        UpdatedAt = now.AddHours(-2)
    };

    private sealed record LifecycleFixture(Guid PublishedId, Guid RunningId);

    public sealed class LifecycleSideEffectProbe
    {
        public int Reconciliations;
    }

    public sealed class LifecycleSideEffectHandler(LifecycleSideEffectProbe probe)
    {
        public void Handle(ReconcileRunnerAssignments message)
        {
            _ = message;
            Interlocked.Increment(ref probe.Reconciliations);
        }

        public void Handle(ProvisionCompetitionRuntimes message) => _ = message;

        public void Handle(CleanupCompetitionRuntimes message) => _ = message;

        public void Handle(CompetitionEventCommitted message) => _ = message;
    }

    public interface IQueueServiceLocationProbe
    {
        void Mark(WorkerQueue? queue);
    }

    public sealed class QueueServiceLocationProbe : IQueueServiceLocationProbe
    {
        private int total;

        public int Total => Volatile.Read(ref total);

        public void Mark(WorkerQueue? queue)
        {
            _ = queue;
            Interlocked.Increment(ref total);
        }
    }

    public sealed record ControlServiceLocationProbe;

    public sealed record GameplayServiceLocationProbe;

    public sealed record ProjectionServiceLocationProbe;

    public sealed record BackgroundServiceLocationProbe;

    public sealed record RunnerServiceLocationProbe;

    public sealed class QueueServiceLocationHandler(IQueueServiceLocationProbe probe)
    {
        public void Handle(ControlServiceLocationProbe message)
        {
            _ = message;
            probe.Mark(WorkerQueue.Control);
        }

        public void Handle(GameplayServiceLocationProbe message)
        {
            _ = message;
            probe.Mark(WorkerQueue.Gameplay);
        }

        public void Handle(ProjectionServiceLocationProbe message)
        {
            _ = message;
            probe.Mark(WorkerQueue.Projection);
        }

        public void Handle(BackgroundServiceLocationProbe message)
        {
            _ = message;
            probe.Mark(WorkerQueue.Background);
        }

        public void Handle(RunnerServiceLocationProbe message)
        {
            _ = message;
            probe.Mark(null);
        }
    }
}
