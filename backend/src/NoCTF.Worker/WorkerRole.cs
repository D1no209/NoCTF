using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Observability;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Hosting.Messaging;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine.Runtime.Agents;

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
        services.AddTransient<GameplayFactMessageHandler>();
        services.AddTransient<LeaderboardMessageHandler>();
        services.AddTransient<FileCleanupMessageHandler>();
        services.AddTransient<Competitions.CompetitionDeletionMessageHandler>();
        services.AddTransient<AwdMessageHandler>();
        services.AddTransient<CompetitionLifecycleMessageHandler>();
        services.AddTransient<AwdpMessageHandler>();
        services.AddTransient<RuntimeDispatchMessageHandler>();
        services.AddTransient<QueuedRuntimeDispatchHandler>();
        services.AddSingleton<NoCTF.Infrastructure.Runtime.Capacity.RuntimeDispatchWakeupGate>();
        services.AddTransient<ReleaseRunnerCapacityHandler>();
        services.AddTransient<GameplayFactDrainMessageHandler>();
        services.AddSingleton<LeaderboardProjectionMergeQueue>();
        if (WorkerQueues.GetEnabled(configuration).Contains(WorkerQueue.Background))
        {
            services.AddScoped<IReadinessDependency,
                AccountNotificationReadinessDependency>();
        }
        if (enableClusterScheduling)
        {
            services.AddSingleton<ClusterSchedulingState>();
            services.AddSingleton(new ClusterSchedulerNodeIdentity(
                $"{Environment.MachineName}:{Environment.ProcessId}"));
            services.AddSingleton<IClusterSchedulerStatusStore, RedisClusterSchedulerStatusStore>();
            services.AddScoped<IClusterScheduleSource, PostgresClusterScheduleSource>();
            services.AddSingleton<IReadinessDependency, ClusterSchedulingReadinessDependency>();
            services.AddSingularAgent<MaintenanceTickAgent>();
        }
        // Queue depth is now observed from JetStream consumer metrics. The old
        // PostgreSQL table poller must not be registered in a NATS deployment.
        if (validateMessageTopology)
            services.AddHostedService<WorkerMessageTopologyStartupValidator>();
        return services;
    }

    public static IServiceCollection AddNoCtfWorkerLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        PlatformLogService service = PlatformLogService.Worker) =>
        services.AddNoCtfPlatformLogging(configuration, service);

    public static void ConfigureNoCtfWorkerMessaging(
        this WolverineOptions options,
        IConfiguration configuration,
        bool durable = true)
    {
        options.Discovery.IncludeType(typeof(FileCleanupMessageHandler));
        options.Discovery.IncludeType(typeof(Competitions.CompetitionDeletionMessageHandler));
        options.Discovery.IncludeType(typeof(AwdMessageHandler));
        options.Discovery.IncludeType(typeof(CompetitionLifecycleMessageHandler));
        options.Discovery.IncludeType(typeof(AwdpMessageHandler));
        options.Discovery.IncludeType(typeof(RuntimeDispatchMessageHandler));
        options.Discovery.IncludeType(typeof(QueuedRuntimeDispatchHandler));
        options.Discovery.IncludeType(typeof(ReleaseRunnerCapacityHandler));
        options.Discovery.IncludeType(typeof(GameplayFactDrainMessageHandler));
        options.Discovery.IncludeType(typeof(AccountNotificationMessageHandler));
        options.Discovery.IncludeType(typeof(GameplayFactMessageHandler));
        options.Discovery.IncludeType(typeof(LeaderboardMessageHandler));
        options.Discovery.IncludeType(typeof(CompetitionNotificationMessageHandlers));
        var enabledQueues = WorkerQueues.GetEnabled(configuration);
        if (enabledQueues.Contains(WorkerQueue.Background))
            options.Discovery.IncludeType(typeof(CompetitionEventRealtimeMessageHandler));
        if (enabledQueues.Contains(WorkerQueue.Projection))
            options.Discovery.IncludeType(typeof(CompetitionEventLeaderboardMessageHandler));
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
        if (!durable)
            return;

        foreach (var queue in enabledQueues)
        {
            var queueName = WorkerQueues.GetName(queue);
            options.ListenToNatsSubject(NatsSubjects.Subject(queue))
                .UseJetStream(NatsSubjects.Stream(queue), queueName)
                .Named(queueName)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(configuration, queue))
                .UseDurableInbox();
        }

        if (enabledQueues.Contains(WorkerQueue.Background))
        {
            options.ListenToNatsSubject(NatsSubjects.RealtimeEvents)
                .UseJetStream(NatsSubjects.EventsStream, "noctf-realtime")
                .Named(CompetitionEventFanoutQueueNames.Realtime)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(
                    configuration,
                    WorkerQueue.Background))
                .UseDurableInbox();
        }
        if (enabledQueues.Contains(WorkerQueue.Projection))
        {
            options.ListenToNatsSubject(NatsSubjects.LeaderboardEvents)
                .UseJetStream(NatsSubjects.EventsStream, "noctf-leaderboard")
                .Named(CompetitionEventFanoutQueueNames.Leaderboard)
                .ListenOnlyAtLeader()
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(
                    configuration,
                    WorkerQueue.Projection))
                .UseDurableInbox();
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
