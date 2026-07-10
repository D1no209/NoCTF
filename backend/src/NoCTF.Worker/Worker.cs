using NoCTF.Application.BackgroundTasks;

namespace NoCTF.Worker;

public class Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NoCTF worker started.");
        await RecoverExpiredTasksAsync(stoppingToken);
        var nextRecoveryAt = DateTime.UtcNow.AddMinutes(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextRecoveryAt)
                {
                    await RecoverExpiredTasksAsync(stoppingToken);
                    nextRecoveryAt = DateTime.UtcNow.AddMinutes(1);
                }

                using var scope = scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
                var task = await queue.TryAcquireNextAsync(LockDuration, stoppingToken);

                if (task is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                try
                {
                    var registry = scope.ServiceProvider.GetRequiredService<ICompetitionJobRegistry>();
                    var handler = registry.GetRequiredHandler(task.Type);
                    var lockOwner = task.LockOwner
                        ?? throw new InvalidOperationException($"Background task {task.Id} has no lease owner.");
                    using var workCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var renewalTask = RenewLeaseUntilCancelledAsync(task.Id, lockOwner, workCts.Token);
                    try
                    {
                        var handlerTask = handler.ExecuteAsync(task, workCts.Token);
                        var completed = await Task.WhenAny(handlerTask, renewalTask);
                        if (completed == renewalTask)
                        {
                            await renewalTask;
                            throw new InvalidOperationException($"Lease renewal stopped unexpectedly for background task {task.Id}.");
                        }
                        await handlerTask;
                    }
                    finally
                    {
                        await workCts.CancelAsync();
                        try
                        {
                            await renewalTask;
                        }
                        catch (OperationCanceledException) when (workCts.IsCancellationRequested)
                        {
                        }
                    }
                    await queue.MarkSucceededAsync(task.Id, lockOwner, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background task {TaskId} of type {TaskType} failed.", task.Id, task.Type);
                    if (!string.IsNullOrWhiteSpace(task.LockOwner))
                        await queue.MarkFailedAsync(task.Id, task.LockOwner, ex, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RenewLeaseUntilCancelledAsync(Guid taskId, string lockOwner, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
            if (!await queue.RenewLeaseAsync(taskId, lockOwner, LockDuration, ct))
                throw new InvalidOperationException($"Lost lease for background task {taskId}.");
        }
    }

    private async Task RecoverExpiredTasksAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
        var recovered = await queue.RecoverExpiredRunningTasksAsync(LockDuration, stoppingToken);
        if (recovered > 0)
            logger.LogWarning("Recovered {Count} expired running background tasks.", recovered);
    }

}
