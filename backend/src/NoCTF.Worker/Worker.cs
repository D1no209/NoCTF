using NoCTF.Application.BackgroundTasks;

namespace NoCTF.Worker;

public class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger,
    IConfiguration configuration) : BackgroundService
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Clamp(configuration.GetValue("Worker:Concurrency", 4), 1, 32);
        var longRunningConcurrency = Math.Clamp(
            configuration.GetValue("Worker:LongRunningConcurrency", 1),
            1,
            8);
        var longRunningTypes = GetLongRunningTaskTypes();
        logger.LogInformation(
            "NoCTF worker started with {Concurrency} standard processors and {LongRunningConcurrency} long-running processors for {LongRunningTypeCount} task types.",
            concurrency,
            longRunningConcurrency,
            longRunningTypes.Count);
        await RecoverExpiredTasksAsync(stoppingToken);
        var longRunningProcessors = longRunningTypes.Count == 0
            ? []
            : Enumerable.Range(0, longRunningConcurrency)
                .Select(index => ProcessTasksAsync(index, longRunningTypes, null, stoppingToken))
                .ToArray();
        var processors = Enumerable.Range(0, concurrency)
            .Select(index => ProcessTasksAsync(index, null, longRunningTypes, stoppingToken))
            .Concat(longRunningProcessors)
            .Append(RecoverExpiredTasksUntilCancelledAsync(stoppingToken))
            .ToArray();
        await Task.WhenAll(processors);
    }

    private IReadOnlyCollection<string> GetLongRunningTaskTypes()
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<ICompetitionJobRegistry>()
            .Jobs
            .Where(job => job.Workload == CompetitionJobWorkload.LongRunning)
            .Select(job => job.JobKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task ProcessTasksAsync(
        int processorId,
        IReadOnlyCollection<string>? includedTypes,
        IReadOnlyCollection<string>? excludedTypes,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
                var task = await queue.TryAcquireNextAsync(
                    LockDuration,
                    includedTypes,
                    excludedTypes,
                    stoppingToken);

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
                    await ExecuteWithLeaseRenewalAsync(
                        task.Id,
                        lockOwner,
                        token => handler.ExecuteAsync(task, token),
                        stoppingToken);
                    // The handler may have completed an external side effect just
                    // as the host begins shutting down. Persist completion without
                    // the request lifetime token so the task is not replayed.
                    await queue.MarkSucceededAsync(task.Id, lockOwner, CancellationToken.None);
                }
                catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
                {
                    if (!string.IsNullOrWhiteSpace(task.LockOwner))
                        await queue.MarkFailedAsync(task.Id, task.LockOwner, ex, CancellationToken.None);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Worker processor {ProcessorId}: background task {TaskId} of type {TaskType} failed.",
                        processorId,
                        task.Id,
                        task.Type);
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
                logger.LogError(ex, "Worker processor {ProcessorId} loop failed.", processorId);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RecoverExpiredTasksUntilCancelledAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RecoverExpiredTasksAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
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

    private async Task ExecuteWithLeaseRenewalAsync(
        Guid taskId,
        string lockOwner,
        Func<CancellationToken, Task> execute,
        CancellationToken stoppingToken)
    {
        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewalTask = RenewLeaseUntilCancelledAsync(taskId, lockOwner, workCts.Token);
        Task handlerTask;
        try
        {
            handlerTask = execute(workCts.Token);
        }
        catch
        {
            await workCts.CancelAsync();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (workCts.IsCancellationRequested)
            {
            }
            throw;
        }
        var completed = await Task.WhenAny(handlerTask, renewalTask);

        if (completed == renewalTask)
        {
            Exception? leaseFailure = null;
            try
            {
                await renewalTask;
                if (!stoppingToken.IsCancellationRequested)
                {
                    leaseFailure = new InvalidOperationException(
                        $"Lease renewal stopped unexpectedly for background task {taskId}.");
                }
            }
            catch (OperationCanceledException) when (workCts.IsCancellationRequested)
            {
                // Cancellation still requires joining the handler below. This
                // prevents a detached mutation from continuing after the task
                // has been returned to the retry queue during shutdown.
            }
            catch (Exception ex)
            {
                leaseFailure = ex;
            }

            await workCts.CancelAsync();
            var handlerCanceled = false;
            try
            {
                await handlerTask;
            }
            catch (OperationCanceledException) when (workCts.IsCancellationRequested)
            {
                handlerCanceled = true;
            }
            catch (Exception handlerFailure)
            {
                if (stoppingToken.IsCancellationRequested)
                    throw;

                throw new AggregateException(
                    $"Background task {taskId} failed after its lease was lost.",
                    leaseFailure ?? new InvalidOperationException("Background task lease renewal stopped."),
                    handlerFailure);
            }

            if (stoppingToken.IsCancellationRequested && handlerCanceled)
                throw new OperationCanceledException(stoppingToken);
            if (stoppingToken.IsCancellationRequested)
                return;

            throw new InvalidOperationException(
                $"Lease renewal failed for background task {taskId}.",
                leaseFailure ?? new InvalidOperationException("Background task lease renewal stopped."));
        }

        try
        {
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
