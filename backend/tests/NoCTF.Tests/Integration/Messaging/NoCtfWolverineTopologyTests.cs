using System.Collections.Concurrent;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Hosting;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("WolverineTopology")]
[NotInParallel]
public sealed class NoCtfWolverineTopologyTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Competition_event_fanout_reaches_each_sticky_destination_once_and_isolates_failure(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_phase7_fanout");
            await postgres.StartAsync(cancellationToken);
            Phase7TopologyObservation.Reset();
            using var host = BuildFanoutHost(postgres.GetConnectionString());
            await host.StartAsync(cancellationToken);
            try
            {
                var successful = CreateEvent();
                await host.Services.GetRequiredService<IMessageBus>().PublishAsync(successful);
                await WaitUntilAsync(
                    () => Phase7TopologyObservation.FanoutCount(successful.EventId) == 2,
                    cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                await Assert.That(Phase7TopologyObservation.DestinationCount(
                        successful.EventId,
                        CompetitionEventFanoutQueueNames.Realtime))
                    .IsEqualTo(1);
                await Assert.That(Phase7TopologyObservation.DestinationCount(
                        successful.EventId,
                        CompetitionEventFanoutQueueNames.Leaderboard))
                    .IsEqualTo(1);
                await Assert.That(Phase7TopologyObservation.EnvelopeIds(successful.EventId).Distinct().Count())
                    .IsEqualTo(2);

                var realtimeFailure = CreateEvent();
                Phase7TopologyObservation.FailRealtime(realtimeFailure.EventId);
                await host.Services.GetRequiredService<IMessageBus>().PublishAsync(realtimeFailure);
                await WaitUntilAsync(
                    () => Phase7TopologyObservation.DestinationCount(
                        realtimeFailure.EventId,
                        CompetitionEventFanoutQueueNames.Leaderboard) == 1,
                    cancellationToken);

                await Assert.That(Phase7TopologyObservation.DestinationCount(
                        realtimeFailure.EventId,
                        CompetitionEventFanoutQueueNames.Leaderboard))
                    .IsEqualTo(1);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task PostgreSQL_queue_survives_restart_and_competing_consumers_execute_each_message_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_phase7_competing");
            await postgres.StartAsync(cancellationToken);
            Phase7TopologyObservation.Reset();
            var connectionString = postgres.GetConnectionString();
            var messages = Enumerable.Range(0, 24)
                .Select(_ => new ProjectLeaderboard(Guid.CreateVersion7()))
                .ToArray();

            using (var producer = BuildProducerHost(connectionString))
            {
                await producer.StartAsync(cancellationToken);
                var bus = producer.Services.GetRequiredService<IMessageBus>();
                foreach (var message in messages)
                    await bus.PublishAsync(message);
                await producer.StopAsync(cancellationToken);
            }

            await postgres.StopAsync(cancellationToken);
            await postgres.StartAsync(cancellationToken);
            var restartedConnectionString = postgres.GetConnectionString();

            using var first = BuildCompetingHost(restartedConnectionString);
            using var second = BuildCompetingHost(restartedConnectionString);
            await first.StartAsync(cancellationToken);
            await second.StartAsync(cancellationToken);
            try
            {
                await WaitUntilAsync(
                    () => messages.All(message =>
                        Phase7TopologyObservation.CompetingCount(message.CompetitionId) == 1),
                    cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                foreach (var message in messages)
                {
                    await Assert.That(Phase7TopologyObservation.CompetingCount(message.CompetitionId))
                        .IsEqualTo(1);
                }
            }
            finally
            {
                await first.StopAsync(cancellationToken);
                await second.StopAsync(cancellationToken);
            }

            using var replayProbe = BuildCompetingHost(restartedConnectionString);
            await replayProbe.StartAsync(cancellationToken);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
                foreach (var message in messages)
                {
                    await Assert.That(Phase7TopologyObservation.CompetingCount(message.CompetitionId))
                        .IsEqualTo(1);
                }
            }
            finally
            {
                await replayProbe.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Missing_sticky_postgresql_endpoint_fails_host_startup(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_phase7_missing_sticky");
            await postgres.StartAsync(cancellationToken);
            using var host = BuildMissingStickyHost(postgres.GetConnectionString());

            var action = () => host.StartAsync(cancellationToken);

            await Assert.That(action).Throws<InvalidOperationException>();
        });
    }

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static IHost BuildFanoutHost(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "background",
                ["Worker:Queues:1"] = "projection"
            })
            .Build();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddHostedService<WorkerMessageTopologyStartupValidator>();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_phase7_fanout");
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<Phase7RealtimeHandler>();
            options.Discovery.IncludeType<Phase7LeaderboardHandler>();
            options.ConfigureNoCtfMessageRouting(
                configuration,
                HostRoles.Only(HostRole.Worker));
            options.ListenToPostgresqlQueue(CompetitionEventFanoutQueueNames.Realtime)
                .Named(CompetitionEventFanoutQueueNames.Realtime)
                .UseDurableInbox();
            options.ListenToPostgresqlQueue(CompetitionEventFanoutQueueNames.Leaderboard)
                .Named(CompetitionEventFanoutQueueNames.Leaderboard)
                .UseDurableInbox()
                .ListenOnlyAtLeader();
        });
        return builder.Build();
    }

    private static IHost BuildProducerHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_phase7_competing");
            options.Discovery.DisableConventionalDiscovery();
            options.ConfigureNoCtfMessageRouting(
                new ConfigurationBuilder().Build(),
                HostRoles.Only(HostRole.Worker));
        });
        return builder.Build();
    }

    private static IHost BuildCompetingHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_phase7_competing");
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<Phase7CompetingHandler>();
            options.ConfigureNoCtfMessageRouting(
                new ConfigurationBuilder().Build(),
                HostRoles.Only(HostRole.Worker));
            options.ListenToPostgresqlQueue(WorkerQueueNames.Projection)
                .Named(WorkerQueueNames.Projection)
                .UseDurableInbox();
        });
        return builder.Build();
    }

    private static IHost BuildMissingStickyHost(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "background"
            })
            .Build();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddHostedService<WorkerMessageTopologyStartupValidator>();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_phase7_missing_sticky");
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<Phase7RealtimeHandler>();
            options.ConfigureNoCtfMessageRouting(
                configuration,
                HostRoles.Only(HostRole.Worker));
        });
        return builder.Build();
    }

    private static void ConfigurePersistence(
        WolverineOptions options,
        string connectionString,
        string schema)
    {
        options.ServiceName = $"NoCTF.Phase7.{schema}";
        options.PersistMessagesWithPostgresql(connectionString, schema);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
        options.Durability.CheckAssignmentPeriod = TimeSpan.FromMilliseconds(250);
        options.Durability.FirstHealthCheckExecution = TimeSpan.FromMilliseconds(100);
        options.Durability.HealthCheckPollingTime = TimeSpan.FromMilliseconds(250);
        options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(1);
    }

    private static CompetitionEventCommitted CreateEvent() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        CompetitionEventKind.CompetitionUpdated,
        CompetitionEventLevel.Information,
        DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException("The NoCTF Wolverine phase 7 topology test timed out.");
    }

    [StickyHandler(CompetitionEventFanoutQueueNames.Realtime)]
    public sealed class Phase7RealtimeHandler
    {
        public static void Handle(CompetitionEventCommitted message, Envelope envelope)
        {
            Phase7TopologyObservation.RecordFanout(
                message.EventId,
                CompetitionEventFanoutQueueNames.Realtime,
                envelope.Id);
            if (Phase7TopologyObservation.ShouldFailRealtime(message.EventId))
                throw new Phase7RealtimeFailureException();
        }
    }

    [StickyHandler(CompetitionEventFanoutQueueNames.Leaderboard)]
    public sealed class Phase7LeaderboardHandler
    {
        public static void Handle(CompetitionEventCommitted message, Envelope envelope) =>
            Phase7TopologyObservation.RecordFanout(
                message.EventId,
                CompetitionEventFanoutQueueNames.Leaderboard,
                envelope.Id);
    }

    public sealed class Phase7CompetingHandler
    {
        public static void Handle(ProjectLeaderboard message) =>
            Phase7TopologyObservation.RecordCompeting(message.CompetitionId);
    }

    public sealed class Phase7RealtimeFailureException : Exception;

    private static class Phase7TopologyObservation
    {
        private static readonly ConcurrentDictionary<(Guid Id, string Destination), int>
            Fanout = new();
        private static readonly ConcurrentDictionary<Guid, ConcurrentBag<Guid>> EnvelopeIdsByEvent =
            new();
        private static readonly ConcurrentDictionary<Guid, int> Competing = new();
        private static readonly ConcurrentDictionary<Guid, byte> RealtimeFailures = new();

        public static void Reset()
        {
            Fanout.Clear();
            EnvelopeIdsByEvent.Clear();
            Competing.Clear();
            RealtimeFailures.Clear();
        }

        public static void RecordFanout(Guid id, string destination, Guid envelopeId)
        {
            Fanout.AddOrUpdate((id, destination), 1, static (_, count) => count + 1);
            EnvelopeIdsByEvent.GetOrAdd(id, static _ => []).Add(envelopeId);
        }

        public static void RecordCompeting(Guid id) =>
            Competing.AddOrUpdate(id, 1, static (_, count) => count + 1);

        public static void FailRealtime(Guid id) => RealtimeFailures.TryAdd(id, 0);

        public static bool ShouldFailRealtime(Guid id) => RealtimeFailures.ContainsKey(id);

        public static int DestinationCount(Guid id, string destination) =>
            Fanout.TryGetValue((id, destination), out var count) ? count : 0;

        public static int FanoutCount(Guid id) => Fanout
            .Where(item => item.Key.Id == id)
            .Sum(item => item.Value);

        public static IReadOnlyCollection<Guid> EnvelopeIds(Guid id) =>
            EnvelopeIdsByEvent.TryGetValue(id, out var values) ? values : [];

        public static int CompetingCount(Guid id) =>
            Competing.TryGetValue(id, out var count) ? count : 0;
    }
}
