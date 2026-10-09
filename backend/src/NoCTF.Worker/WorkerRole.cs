using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Hosting.Messaging;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Worker.Composition;
using NoCTF.Worker.Competitions.Webhooks;

namespace NoCTF.Worker;

public static class WorkerRole
{
    public static IServiceCollection AddNoCtfWorkerRole(
        this IServiceCollection services,
        IConfiguration configuration,
        bool collectQueueMetrics = true,
        bool validateMessageTopology = true,
        bool enableClusterScheduling = true)
    {
        services.AddOptions<WorkerQueueOptions>()
            .Configure(options =>
            {
                var enabled = WorkerQueues.GetEnabled(configuration);
                options.Enabled = enabled;
                options.Concurrency = enabled.ToDictionary(
                    queue => queue,
                    queue => WorkerQueues.GetConcurrency(configuration, queue));
            })
            .Validate(options => options.Enabled.Count > 0,
                "Worker:Queues must select at least one queue.")
            .ValidateOnStart();
        services.AddTransient<AccountNotificationMessageHandler>();
        services.AddTransient<Authentication.MfaMailHandler>();
        services.AddTransient<Authentication.MfaAuthenticationChangedHandler>();
        services.AddTransient<GameplayFactMessageHandler>();
        services.AddTransient<LiveSolo.LiveSoloRoundMessageHandler>();
        services.AddTransient<LiveSolo.LiveSoloRealtimeMessageHandler>();
        services.AddTransient<LiveSolo.LiveSoloMediaMessageHandler>();
        services.AddTransient<LiveSolo.LiveSoloCaptureMessageHandler>();
        services.AddTransient<LeaderboardMessageHandler>();
        services.AddTransient<FileCleanupMessageHandler>();
        services.AddTransient<Competitions.CompetitionDeletionMessageHandler>();
        services.AddTransient<AwdMessageHandler>();
        services.AddTransient<KohPollingHandler>();
        services.AddTransient<KohObservationHandler>();
        services.AddTransient<CompetitionLifecycleMessageHandler>();
        services.AddTransient<AwdpMessageHandler>();
        services.AddTransient<RuntimeDispatchMessageHandler>();
        services.AddTransient<QueuedRuntimeDispatchHandler>();
        services.AddTransient<PendingGameplayFactDispatchHandler>();
        services.AddRuntimeDispatchWakeupGate(enableClusterScheduling);
        services.AddTransient<ReleaseRunnerCapacityHandler>();
        services.AddTransient<GameplayFactDrainMessageHandler>();
        services.AddTransient<CompetitionWebhookMessageHandler>();
        if (WorkerQueues.GetEnabled(configuration).Contains(WorkerQueue.Webhook))
        {
            services.AddHostedService<CompetitionWebhookOutboxAgent>();
            services.AddHostedService<CompetitionWebhookRetryAgent>();
            services.AddHostedService<NoCTF.Worker.Competitions.StaffWebhooks.StaffWebhookAgent>();
        }
        services.AddSingleton<LeaderboardProjectionMergeQueue>();
        if (WorkerQueues.GetEnabled(configuration).Contains(WorkerQueue.Projection))
            services.AddHostedService<LeaderboardProjectionDispatchAgent>();
        if (WorkerQueues.GetEnabled(configuration).Contains(WorkerQueue.Background))
        {
            services.AddHostedService<Authentication.MfaPendingAgent>();
            services.AddScoped<IReadinessDependency,
                AccountNotificationReadinessDependency>();
        }
        if (enableClusterScheduling)
        {
            services.AddSingleton<ClusterSchedulingState>();
            services.AddSingleton(new ClusterSchedulerNodeIdentity(
                $"{Environment.MachineName}:{Environment.ProcessId}"));
            services.AddSingleton<IClusterSchedulerStatusStore, NatsClusterSchedulerStatusStore>();
            services.AddScoped<IClusterScheduleSource, ClusterScheduleSource>();
            services.AddSingleton<IReadinessDependency, ClusterSchedulingReadinessDependency>();
            services.AddSingleton<NatsClusterLeaseManager>();
            services.AddHostedService<MaintenanceTickAgent>();
        }
        // Queue depth is now observed from JetStream consumer metrics. The old
        // PostgreSQL table poller must not be registered in a NATS deployment.
        if (validateMessageTopology)
            services.AddHostedService<WorkerMessageTopologyStartupValidator>();
        return services;
    }

    public static void ConfigureNoCtfWorkerMessaging(
        this WolverineOptions options,
        IConfiguration configuration,
        bool durable = true)
    {
        options.Discovery.IncludeType(typeof(FileCleanupMessageHandler));
        options.Discovery.IncludeType(typeof(Authentication.MfaMailHandler));
        options.Discovery.IncludeType(typeof(Authentication.MfaAuthenticationChangedHandler));
        options.Discovery.IncludeType(typeof(Competitions.CompetitionDeletionMessageHandler));
        options.Discovery.IncludeType(typeof(AwdMessageHandler));
        options.Discovery.IncludeType(typeof(KohPollingHandler));
        options.Discovery.IncludeType(typeof(KohObservationHandler));
        options.Discovery.IncludeType(typeof(CompetitionLifecycleMessageHandler));
        options.Discovery.IncludeType(typeof(AwdpMessageHandler));
        options.Discovery.IncludeType(typeof(RuntimeDispatchMessageHandler));
        options.Discovery.IncludeType(typeof(QueuedRuntimeDispatchHandler));
        options.Discovery.IncludeType(typeof(PendingGameplayFactDispatchHandler));
        options.Discovery.IncludeType(typeof(ExpireAccountSourceAddressesHandler));
        options.Discovery.IncludeType(typeof(ReleaseRunnerCapacityHandler));
        options.Discovery.IncludeType(typeof(GameplayFactDrainMessageHandler));
        options.Discovery.IncludeType(typeof(AccountNotificationMessageHandler));
        options.Discovery.IncludeType(typeof(GameplayFactMessageHandler));
        options.Discovery.IncludeType(typeof(LiveSolo.LiveSoloRoundMessageHandler));
        options.Discovery.IncludeType(typeof(LiveSolo.LiveSoloRealtimeMessageHandler));
        options.Discovery.IncludeType(typeof(LiveSolo.LiveSoloMediaMessageHandler));
        options.Discovery.IncludeType(typeof(LiveSolo.LiveSoloCaptureMessageHandler));
        options.Discovery.IncludeType(typeof(LeaderboardMessageHandler));
        options.Discovery.IncludeType(typeof(CompetitionWebhookMessageHandler));
        options.Discovery.IncludeType(typeof(Competitions.StaffWebhooks.StaffWebhookMessageHandler));
        options.Discovery.IncludeType(typeof(CompetitionNotificationMessageHandlers));
        var enabledQueues = WorkerQueues.GetEnabled(configuration);
        if (enabledQueues.Contains(WorkerQueue.Background))
            options.Discovery.IncludeType(typeof(CompetitionEventRealtimeMessageHandler));
        if (enabledQueues.Contains(WorkerQueue.Projection))
            options.Discovery.IncludeType(typeof(CompetitionEventLeaderboardMessageHandler));
        if (enabledQueues.Contains(WorkerQueue.Webhook))
            options.Discovery.IncludeType(typeof(CompetitionEventWebhookMessageHandler));
        options.Durability.Mode = DurabilityMode.Balanced;
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
        options.Durability.CheckAssignmentPeriod = TimeSpan.FromSeconds(1);
        options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
        options.Durability.ScheduledJobFirstExecution = TimeSpan.FromSeconds(1);
        options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
        options.Durability.DurabilityMetricsEnabled = true;
        options.Durability.UpdateMetricsPeriod = TimeSpan.FromSeconds(5);
        if (options.MultipleHandlerBehavior == MultipleHandlerBehavior.Separated)
        {
            throw new InvalidOperationException(
                "NoCTF requires Wolverine's ClassicCombineIntoOneLogicalHandler behavior.");
        }
        options.Policies.OnException<EmailVerificationDeliveryException>(
                exception => exception.Failure is
                    EmailVerificationDeliveryFailure.ConnectionFailed
                    or EmailVerificationDeliveryFailure.TimedOut
                    or EmailVerificationDeliveryFailure.TransportFailed)
            .ScheduleRetry(ScheduledEmailRetryDelays)
            .WithFullJitter();
        options.Policies.OnException<EmailVerificationDeliveryException>(
                exception => exception.Failure is
                    EmailVerificationDeliveryFailure.AuthenticationFailed
                    or EmailVerificationDeliveryFailure.MessageRejected)
            .MoveToErrorQueue();
        options.Policies.OnException<CompetitionWebhookTransientException>()
            .ScheduleRetry([
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(2),
                TimeSpan.FromHours(8),
                TimeSpan.FromHours(24)
            ])
            .WithFullJitter();
        options.Policies.OnException<CompetitionWebhookProjectionNotReadyException>()
            .ScheduleRetry([
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5)
            ]);
        options.Policies.OnException<CompetitionWebhookPermanentException>()
            .MoveToErrorQueue();
        if (!durable)
            return;

        foreach (var queue in enabledQueues)
        {
            var queueName = WorkerQueues.GetName(queue);
            options.ListenToNatsSubject(NatsSubjects.Subject(queue))
                .UseJetStream(NatsSubjects.Stream(queue), queueName)
                .Named(queueName)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(configuration, queue));
        }

        if (enabledQueues.Contains(WorkerQueue.Background))
        {
            options.ListenToNatsSubject(NatsSubjects.RealtimeEvents)
                .UseJetStream(NatsSubjects.EventsStream, "noctf-realtime")
                .Named(CompetitionEventFanoutQueueNames.Realtime)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(
                    configuration,
                    WorkerQueue.Background));
        }
        if (enabledQueues.Contains(WorkerQueue.Projection))
        {
            options.ListenToNatsSubject(NatsSubjects.LeaderboardEvents)
                .UseJetStream(NatsSubjects.EventsStream, "noctf-leaderboard")
                .Named(CompetitionEventFanoutQueueNames.Leaderboard)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(
                    configuration,
                    WorkerQueue.Projection));
        }
        if (enabledQueues.Contains(WorkerQueue.Webhook))
        {
            options.ListenToNatsSubject(NatsSubjects.WebhookEvents)
                .UseJetStream(NatsSubjects.EventsStream, "noctf-webhook-events")
                .Named(CompetitionEventFanoutQueueNames.Webhook)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(
                    configuration,
                    WorkerQueue.Webhook));
        }
    }

    private static readonly TimeSpan[] ScheduledEmailRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5)
    ];

}
