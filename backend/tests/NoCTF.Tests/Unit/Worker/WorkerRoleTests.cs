using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Messaging;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
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
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.Webhook)).IsEqualTo(32);
        await Assert.That(WorkerQueues.GetConcurrency(configuration, WorkerQueue.LiveSoloMedia)).IsEqualTo(4);
    }
    [Test]
    public async Task Shipped_worker_configuration_enables_every_typed_queue_including_media()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        var path=Path.Combine(root?.FullName??throw new DirectoryNotFoundException(),"backend","src","NoCTF.API","appsettings.json");
        var configuration=new ConfigurationBuilder().AddJsonFile(path).Build();
        await Assert.That(WorkerQueues.GetEnabled(configuration)).IsEquivalentTo(WorkerQueues.All);
    }

    [Test]
    public async Task Worker_queue_selection_and_concurrency_are_configuration_driven()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:Queues:0"] = "control",
                ["Worker:Queues:1"] = "gameplay",
                ["Worker:Queues:2"] = "livesolo-media",
                ["Worker:Concurrency:Control"] = "1",
                ["Worker:Concurrency:Gameplay"] = "12"
            })
            .Build();

        var queues = WorkerQueues.GetEnabled(configuration);

        await Assert.That(queues).IsEquivalentTo(
            new[] { WorkerQueue.Control, WorkerQueue.Gameplay, WorkerQueue.LiveSoloMedia });
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
    [Arguments(CompetitionEventFanoutQueueNames.Realtime, "nats://subject/noctf.v2.events.realtime")]
    [Arguments(CompetitionEventFanoutQueueNames.Leaderboard, "nats://subject/noctf.v2.events.leaderboard")]
    [Arguments(CompetitionEventFanoutQueueNames.Webhook, "nats://subject/noctf.v2.events.webhook")]
    public async Task Sticky_fanout_uses_the_canonical_NATS_subject_address(
        string queueName,
        string expected)
    {
        var endpoint = WorkerMessageTopologyStartupValidator.NatsEndpointAddress(queueName);

        await Assert.That(endpoint).IsEqualTo(expected);
        WorkerMessageTopologyStartupValidator.ValidateFanoutRouting([queueName], endpoint);
    }

    [Test]
    public async Task Account_notifications_use_the_canonical_background_NATS_route()
    {
        var endpoint = WorkerMessageTopologyStartupValidator.BackgroundEndpointAddress();

        await Assert.That(endpoint).IsEqualTo("nats://subject/noctf.v2.background");
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
            typeof(SendPasswordReset), [typeof(AccountNotificationMessageHandler)]);
        WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes(
            typeof(SendMfaMail), [typeof(NoCTF.Worker.Authentication.MfaMailHandler)]);
        await Assert.That(() => WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes(
            typeof(SendMfaMail), [typeof(AccountNotificationMessageHandler)]))
            .Throws<InvalidOperationException>();

        await Assert.That(() =>
                WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes(typeof(SendPasswordReset), []))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                WorkerMessageTopologyStartupValidator.ValidateAccountNotificationHandlerTypes(
                    typeof(SendPasswordReset), [typeof(AccountNotificationMessageHandler), typeof(AccountNotificationMessageHandler)]))
            .Throws<InvalidOperationException>();
    }
}
