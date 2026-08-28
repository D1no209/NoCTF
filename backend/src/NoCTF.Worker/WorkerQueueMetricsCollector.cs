using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class WorkerQueueMetricsCollector(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerQueueOptions> queueOptions,
    TimeProvider timeProvider,
    ILogger<WorkerQueueMetricsCollector> logger) : BackgroundService
{
    private const string QueueSchema = "wolverine_queues";
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

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            var connection = db.Database.GetDbConnection();
            var closeConnection = connection.State != ConnectionState.Open;
            if (closeConnection)
                await connection.OpenAsync(cancellationToken);

            try
            {
                foreach (var queue in monitoredQueues)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = BuildSnapshotSql(queue);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                        continue;

                    var depth = reader.GetInt64(0);
                    var oldestAgeSeconds = reader.GetDouble(1);
                    NoCtfTelemetry.UpdateWorkerQueueSnapshot(
                        queue,
                        depth,
                        TimeSpan.FromSeconds(Math.Max(0, oldestAgeSeconds)));
                }
            }
            finally
            {
                if (closeConnection)
                    await connection.CloseAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Worker queue metric collection failed.");
        }
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

    private static string BuildSnapshotSql(string queue)
    {
        if (!WorkerQueueMonitoringNames.IsKnown(queue))
            throw new ArgumentOutOfRangeException(nameof(queue), queue, "Unknown worker queue.");
        var tableName = queue.Replace('-', '_');
        return $"""
            SELECT COUNT(*)::bigint,
                   COALESCE(
                       EXTRACT(EPOCH FROM ((NOW() AT TIME ZONE 'utc') - MIN(timestamp))),
                       0)::double precision
            FROM {QueueSchema}.wolverine_queue_{tableName}
            """;
    }
}
