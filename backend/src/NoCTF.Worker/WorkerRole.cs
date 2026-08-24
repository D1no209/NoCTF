using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using Wolverine.Postgresql;
using Wolverine.Runtime.Agents;

namespace NoCTF.Worker;

public static class WorkerRole
{
    public static IServiceCollection AddNoCtfWorkerRole(
        this IServiceCollection services,
        bool collectQueueMetrics = true,
        bool validateMessageTopology = true,
        bool enableClusterScheduling = true)
    {
        services.AddSingleton<LeaderboardProjectionMergeQueue>();
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
        if (collectQueueMetrics)
            services.AddHostedService<WorkerQueueMetricsCollector>();
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
        options.Discovery.IncludeType(typeof(BackendMessageHandlers));
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
            options.ListenToPostgresqlQueue(queueName)
                .Named(queueName)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(configuration, queue))
                .UseDurableInbox();
        }

        if (enabledQueues.Contains(WorkerQueue.Background))
        {
            options.ListenToPostgresqlQueue(CompetitionEventFanoutQueueNames.Realtime)
                .Named(CompetitionEventFanoutQueueNames.Realtime)
                .MaximumParallelMessages(WorkerQueues.GetConcurrency(
                    configuration,
                    WorkerQueue.Background))
                .UseDurableInbox();
        }
        if (enabledQueues.Contains(WorkerQueue.Projection))
        {
            options.ListenToPostgresqlQueue(CompetitionEventFanoutQueueNames.Leaderboard)
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
