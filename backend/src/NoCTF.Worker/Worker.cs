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
                    await handler.ExecuteAsync(task, stoppingToken);
                    await queue.MarkSucceededAsync(task.Id, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background task {TaskId} of type {TaskType} failed.", task.Id, task.Type);
                    await queue.MarkFailedAsync(task.Id, ex, CancellationToken.None);
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

    private async Task RecoverExpiredTasksAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
        var recovered = await queue.RecoverExpiredRunningTasksAsync(LockDuration, stoppingToken);
        if (recovered > 0)
            logger.LogWarning("Recovered {Count} expired running background tasks.", recovered);
    }

}
