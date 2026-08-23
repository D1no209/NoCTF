using System.Collections.Concurrent;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Postgresql;
using Wolverine.Runtime;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("Wolverine6292Spike")]
[NotInParallel]
public sealed class Wolverine6292TopologySpikeTests
{
    private const string FanoutRealtimeQueue = "spike-fanout-realtime";
    private const string FanoutProjectionQueue = "spike-fanout-projection";
    private const string CompetingQueue = "spike-competing";

    [Test]
    [Timeout(300_000)]
    public async Task Sticky_postgresql_fanout_processes_the_same_message_once_per_destination(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            Wolverine6292SpikeObservation.Reset();
            using var host = BuildFanoutHost(postgres.GetConnectionString());
            await host.StartAsync(cancellationToken);
            try
            {
                var message = new FanoutSpikeMessage(Guid.CreateVersion7());
                await host.Services.GetRequiredService<IMessageBus>().PublishAsync(message);

                await WaitUntilAsync(
                    () => Wolverine6292SpikeObservation.FanoutCount(message.Id) == 2,
                    cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                await Assert.That(
                        Wolverine6292SpikeObservation.DestinationCount(
                            message.Id,
                            FanoutRealtimeQueue))
                    .IsEqualTo(1);
                await Assert.That(
                        Wolverine6292SpikeObservation.DestinationCount(
                            message.Id,
                            FanoutProjectionQueue))
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
    public async Task PostgreSQL_competing_consumers_process_each_message_once_across_two_workers(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            Wolverine6292SpikeObservation.Reset();
            var connectionString = postgres.GetConnectionString();
            using var first = BuildCompetingConsumerHost(connectionString);
            using var second = BuildCompetingConsumerHost(connectionString);
            await first.StartAsync(cancellationToken);
            await second.StartAsync(cancellationToken);
            try
            {
                var messages = Enumerable.Range(0, 24)
                    .Select(_ => new CompetingSpikeMessage(Guid.CreateVersion7()))
                    .ToArray();
                var bus = first.Services.GetRequiredService<IMessageBus>();
                foreach (var message in messages)
                    await bus.PublishAsync(message);

                await WaitUntilAsync(
                    () => messages.All(message =>
                        Wolverine6292SpikeObservation.CompetingCount(message.Id) == 1),
                    cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                foreach (var message in messages)
                    await Assert.That(
                            Wolverine6292SpikeObservation.CompetingCount(message.Id))
                        .IsEqualTo(1);
            }
            finally
            {
                await first.StopAsync(cancellationToken);
                await second.StopAsync(cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Missing_sticky_endpoint_falls_back_to_a_local_queue_in_6292(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres();
            await postgres.StartAsync(cancellationToken);
            using var host = BuildMissingStickyEndpointHost(postgres.GetConnectionString());
            await host.StartAsync(cancellationToken);
            try
            {
                var explanation = host.Services.GetRequiredService<IWolverineRuntime>()
                    .ExplainRoutingFor(typeof(MissingStickyEndpointSpikeMessage))
                    .ToText();

                await Assert.That(explanation).Contains("local://");
                await Assert.That(explanation).DoesNotContain("postgresql://spike-missing-sticky");
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    private static PostgreSqlContainer CreatePostgres() =>
        new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("noctf_wolverine_6292_spike")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static IHost BuildFanoutHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_spike_fanout");
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<FanoutRealtimeSpikeHandler>();
            options.Discovery.IncludeType<FanoutProjectionSpikeHandler>();
            options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
            options.ListenToPostgresqlQueue(FanoutRealtimeQueue)
                .Named(FanoutRealtimeQueue)
                .UseDurableInbox();
            options.ListenToPostgresqlQueue(FanoutProjectionQueue)
                .Named(FanoutProjectionQueue)
                .UseDurableInbox();
        });
        return builder.Build();
    }

    private static IHost BuildCompetingConsumerHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_spike_competing");
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<CompetingSpikeHandler>();
            options.ListenToPostgresqlQueue(CompetingQueue)
                .Named(CompetingQueue)
                .UseDurableInbox();
            options.PublishMessage<CompetingSpikeMessage>()
                .ToPostgresqlQueue(CompetingQueue);
        });
        return builder.Build();
    }

    private static IHost BuildMissingStickyEndpointHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            ConfigurePersistence(options, connectionString, "wolverine_spike_missing_sticky");
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<MissingStickyEndpointSpikeHandler>();
        });
        return builder.Build();
    }

    private static void ConfigurePersistence(
        WolverineOptions options,
        string connectionString,
        string schema)
    {
        options.PersistMessagesWithPostgresql(connectionString, schema);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
        options.Durability.CheckAssignmentPeriod = TimeSpan.FromMilliseconds(250);
        options.Durability.FirstHealthCheckExecution = TimeSpan.FromMilliseconds(100);
        options.Durability.HealthCheckPollingTime = TimeSpan.FromMilliseconds(250);
        options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(1);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException("The Wolverine 6.29.2 topology spike timed out.");
    }

    public sealed record FanoutSpikeMessage(Guid Id);

    public sealed record CompetingSpikeMessage(Guid Id);

    public sealed record MissingStickyEndpointSpikeMessage(Guid Id);

    [StickyHandler(FanoutRealtimeQueue)]
    public sealed class FanoutRealtimeSpikeHandler
    {
        public static void Handle(FanoutSpikeMessage message) =>
            Wolverine6292SpikeObservation.RecordFanout(message.Id, FanoutRealtimeQueue);
    }

    [StickyHandler(FanoutProjectionQueue)]
    public sealed class FanoutProjectionSpikeHandler
    {
        public static void Handle(FanoutSpikeMessage message) =>
            Wolverine6292SpikeObservation.RecordFanout(message.Id, FanoutProjectionQueue);
    }

    public sealed class CompetingSpikeHandler
    {
        public static void Handle(CompetingSpikeMessage message) =>
            Wolverine6292SpikeObservation.RecordCompeting(message.Id);
    }

    [StickyHandler("spike-missing-sticky")]
    public sealed class MissingStickyEndpointSpikeHandler
    {
        public static void Handle(MissingStickyEndpointSpikeMessage message)
        {
        }
    }

    private static class Wolverine6292SpikeObservation
    {
        private static readonly ConcurrentDictionary<(Guid Id, string Destination), int>
            Fanout = new();
        private static readonly ConcurrentDictionary<Guid, int> Competing = new();

        public static void Reset()
        {
            Fanout.Clear();
            Competing.Clear();
        }

        public static void RecordFanout(Guid id, string destination) =>
            Fanout.AddOrUpdate((id, destination), 1, static (_, count) => count + 1);

        public static void RecordCompeting(Guid id) =>
            Competing.AddOrUpdate(id, 1, static (_, count) => count + 1);

        public static int DestinationCount(Guid id, string destination) =>
            Fanout.TryGetValue((id, destination), out var count) ? count : 0;

        public static int FanoutCount(Guid id) => Fanout
            .Where(item => item.Key.Id == id)
            .Sum(item => item.Value);

        public static int CompetingCount(Guid id) =>
            Competing.TryGetValue(id, out var count) ? count : 0;
    }
}
