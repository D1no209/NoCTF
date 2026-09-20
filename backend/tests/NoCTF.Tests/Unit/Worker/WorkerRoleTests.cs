using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Messaging;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Worker;
using Wolverine;
using Wolverine.Configuration;

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
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Webhook)).IsEqualTo(8);
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
    public async Task Missing_sticky_nats_listener_is_a_startup_failure()
    {
        var action = () => WorkerMessageTopologyStartupValidator.ValidateFanoutRouting(
            [CompetitionEventFanoutQueueNames.Realtime],
            $"local://{CompetitionEventFanoutQueueNames.Realtime}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Sticky_fanout_rejects_local_fallback_even_when_nats_listener_exists()
    {
        var endpoint = WorkerMessageTopologyStartupValidator.NatsEndpointAddress(
            CompetitionEventFanoutQueueNames.Realtime);
        var action = () => WorkerMessageTopologyStartupValidator.ValidateFanoutRouting(
            [CompetitionEventFanoutQueueNames.Realtime],
            $"{endpoint}{Environment.NewLine}local://{CompetitionEventFanoutQueueNames.Realtime}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Account_delivery_readiness_is_registered_only_for_the_background_queue()
    {
        var background = new ServiceCollection();
        var control = new ServiceCollection();
        var controlConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "control"
            })
            .Build();

        background.AddNoCtfWorkerRole(
            new ConfigurationBuilder().Build(),
            validateMessageTopology: false,
            enableClusterScheduling: false);
        control.AddNoCtfWorkerRole(
            controlConfiguration,
            validateMessageTopology: false,
            enableClusterScheduling: false);

        await Assert.That(background.Any(descriptor =>
                descriptor.ServiceType == typeof(IReadinessDependency)
                && descriptor.ImplementationType
                    == typeof(AccountNotificationReadinessDependency)))
            .IsTrue();
        await Assert.That(control.Any(descriptor =>
                descriptor.ServiceType == typeof(IReadinessDependency)
                && descriptor.ImplementationType
                    == typeof(AccountNotificationReadinessDependency)))
            .IsFalse();
    }

    [Test]
    [Arguments(CompetitionEventFanoutQueueNames.Realtime, "nats://subject/noctf.events.realtime")]
    [Arguments(CompetitionEventFanoutQueueNames.Leaderboard, "nats://subject/noctf.events.leaderboard")]
    [Arguments(CompetitionEventFanoutQueueNames.Webhook, "nats://subject/noctf.events.webhook")]
    public async Task Sticky_fanout_uses_the_canonical_NATS_subject_address(
        string queueName,
        string expected)
    {
        var endpoint = WorkerMessageTopologyStartupValidator.NatsEndpointAddress(queueName);

        await Assert.That(endpoint).IsEqualTo(expected);
        WorkerMessageTopologyStartupValidator.ValidateFanoutRouting([queueName], endpoint);
    }

    [Test]
    public async Task Leaderboard_fanout_rejects_listener_not_pinned_to_leader()
    {
        var action = () => WorkerMessageTopologyStartupValidator.ValidateLeaderboardListenerScope(
            ListenerScope.Exclusive);

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Account_notifications_use_the_canonical_background_NATS_route()
    {
        var endpoint = WorkerMessageTopologyStartupValidator.BackgroundEndpointAddress();

        await Assert.That(endpoint).IsEqualTo("nats://subject/noctf.background");
        WorkerMessageTopologyStartupValidator.ValidateBackgroundRouting(endpoint);
    }

    [Test]
    public async Task Account_notifications_reject_a_local_queue_fallback()
    {
        var endpoint = WorkerMessageTopologyStartupValidator.BackgroundEndpointAddress();
        var action = () => WorkerMessageTopologyStartupValidator.ValidateBackgroundRouting(
            $"{endpoint}{Environment.NewLine}local://{WorkerQueueNames.Background}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Account_notification_messages_require_exactly_one_handler()
    {
        WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes(
            [typeof(AccountNotificationMessageHandler)]);

        await Assert.That(() =>
                WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes([]))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes(
                    [typeof(AccountNotificationMessageHandler), typeof(AccountNotificationMessageHandler)]))
            .Throws<InvalidOperationException>();
    }
}
