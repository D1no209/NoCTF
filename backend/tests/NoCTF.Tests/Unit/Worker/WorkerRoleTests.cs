using Microsoft.Extensions.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Hosting;
using NoCTF.Worker;
using Wolverine;

namespace NoCTF.Tests.Unit.Worker;

public sealed class WorkerRoleTests
{
    [Test]
    public async Task Messaging_starts_singular_maintenance_assignment_promptly()
    {
        var options = new WolverineOptions();
        var configuration = new ConfigurationBuilder().Build();

        options.ConfigureNoCtfWorkerMessaging(configuration, durable: false);

        await Assert.That(options.Durability.CheckAssignmentPeriod)
            .IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.Durability.FirstHealthCheckExecution)
            .IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.Durability.ScheduledJobFirstExecution)
            .IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.Durability.ScheduledJobPollingTime)
            .IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Worker_queues_default_to_all_bounded_categories()
    {
        var configuration = new ConfigurationBuilder().Build();

        var queues = WorkerQueues.GetEnabled(configuration);

        await Assert.That(queues).IsEquivalentTo(WorkerQueues.All);
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Control)).IsEqualTo(2);
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Gameplay)).IsEqualTo(8);
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Projection)).IsEqualTo(2);
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Background)).IsEqualTo(2);
    }

    [Test]
    public async Task Worker_queue_selection_and_concurrency_are_configuration_driven()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "control",
                ["Worker:Queues:1"] = "gameplay",
                ["Worker:Concurrency:Control"] = "1",
                ["Worker:Concurrency:Gameplay"] = "12"
            })
            .Build();

        var queues = WorkerQueues.GetEnabled(configuration);

        await Assert.That(queues).IsEquivalentTo(
            new[] { WorkerQueue.Control, WorkerQueue.Gameplay });
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Control)).IsEqualTo(1);
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Gameplay)).IsEqualTo(12);
    }

    [Test]
    public async Task Unknown_worker_queue_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "mystery"
            })
            .Build();

        var action = () => WorkerQueues.GetEnabled(configuration);

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Queue_metrics_include_only_enabled_workloads_and_their_fanout_destinations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "projection",
                ["Worker:Queues:1"] = "background"
            })
            .Build();

        var queues = WorkerQueueMetricsCollector.ResolveMonitoredQueues(configuration);

        await Assert.That(queues).IsEquivalentTo(new[]
        {
            WorkerQueueNames.Projection,
            WorkerQueueNames.Background,
            CompetitionEventFanoutQueueNames.Realtime,
            CompetitionEventFanoutQueueNames.Leaderboard
        });
    }

    [Test]
    public async Task Missing_sticky_postgresql_listener_is_a_startup_failure()
    {
        var action = () => WorkerMessageTopologyStartupValidator.ValidateFanoutRouting(
            [CompetitionEventFanoutQueueNames.Realtime],
            $"local://{CompetitionEventFanoutQueueNames.Realtime}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Sticky_fanout_rejects_local_fallback_even_when_postgresql_listener_exists()
    {
        var endpoint = WorkerMessageTopologyStartupValidator.PostgresqlQueueAddress(
            CompetitionEventFanoutQueueNames.Realtime);
        var action = () => WorkerMessageTopologyStartupValidator.ValidateFanoutRouting(
            [CompetitionEventFanoutQueueNames.Realtime],
            $"{endpoint}{Environment.NewLine}local://{CompetitionEventFanoutQueueNames.Realtime}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }
}
