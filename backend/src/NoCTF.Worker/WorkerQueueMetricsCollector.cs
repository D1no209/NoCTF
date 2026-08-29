using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Hosting;

namespace NoCTF.Worker;

public sealed class WorkerQueueMetricsCollector(
    IOptions<WorkerQueueOptions> queueOptions,
    TimeProvider timeProvider,
    ILogger<WorkerQueueMetricsCollector> logger) : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(5);
    private readonly IReadOnlyList<string> monitoredQueues =
        ResolveMonitoredQueues(queueOptions.Value.Enabled);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CollectionInterval, timeProvider);
        do
        {
            await CollectAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private Task CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // JetStream queue depth is exported by the NATS monitoring endpoint.
        // Keeping this service as a compatibility shell avoids reintroducing a
        // PostgreSQL queue poller into the worker process.
        logger.LogDebug("NATS queue metrics are collected by the NATS monitoring endpoint for {Count} queues.", monitoredQueues.Count);
        return Task.CompletedTask;
    }

    internal static IReadOnlyList<string> ResolveMonitoredQueues(
        IReadOnlyCollection<WorkerQueue> enabled)
    {
        var queues = enabled.Select(WorkerQueueNames.GetName).ToList();
        if (enabled.Contains(WorkerQueue.Background))
            queues.Add(CompetitionEventFanoutQueueNames.Realtime);
        if (enabled.Contains(WorkerQueue.Projection))
            queues.Add(CompetitionEventFanoutQueueNames.Leaderboard);
        return queues;
    }

}
