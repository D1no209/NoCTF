using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Application.BackgroundTasks;

public interface IBackgroundTaskQueue
{
    Task<Guid> EnqueueAsync<TPayload>(
        Guid competitionId,
        string type,
        TPayload payload,
        CancellationToken cancellationToken = default);

    Task<BackgroundTaskItem?> TryAcquireNextAsync(
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    Task MarkSucceededAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid taskId, Exception exception, CancellationToken cancellationToken = default);
    Task<int> RecoverExpiredRunningTasksAsync(TimeSpan lockTimeout, CancellationToken cancellationToken = default);
}

public class BackgroundTaskQueue(ApplicationDbContext dbContext) : IBackgroundTaskQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> EnqueueAsync<TPayload>(
        Guid competitionId,
        string type,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var task = new BackgroundTaskItem
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Type = type,
            Status = BackgroundTaskStatus.Pending,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.BackgroundTasks.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);
        return task.Id;
    }

    public async Task<BackgroundTaskItem?> TryAcquireNextAsync(
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var task = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                (t.Status == BackgroundTaskStatus.Pending || t.Status == BackgroundTaskStatus.Retrying) &&
                (t.LockedUntil == null || t.LockedUntil < now))
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
            return null;

        task.Status = BackgroundTaskStatus.Running;
        task.AttemptCount += 1;
        task.LockedUntil = now.Add(lockDuration);
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task MarkSucceededAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId, cancellationToken);

        task.Status = BackgroundTaskStatus.Succeeded;
        task.LockedUntil = null;
        task.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid taskId, Exception exception, CancellationToken cancellationToken = default)
    {
        var task = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId, cancellationToken);

        task.LastError = exception.Message;
        task.Status = task.AttemptCount < task.MaxAttempts
            ? BackgroundTaskStatus.Retrying
            : BackgroundTaskStatus.Failed;
        task.LockedUntil = task.Status == BackgroundTaskStatus.Retrying
            ? DateTime.UtcNow.AddSeconds(Math.Min(300, 10 * task.AttemptCount))
            : null;
        task.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RecoverExpiredRunningTasksAsync(TimeSpan lockTimeout, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredBefore = now.Subtract(lockTimeout);
        var expired = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                t.Status == BackgroundTaskStatus.Running &&
                (t.LockedUntil == null || t.LockedUntil < now || t.UpdatedAt < expiredBefore))
            .ToListAsync(cancellationToken);

        foreach (var task in expired)
        {
            task.Status = task.AttemptCount < task.MaxAttempts
                ? BackgroundTaskStatus.Retrying
                : BackgroundTaskStatus.Failed;
            task.LockedUntil = null;
            task.UpdatedAt = now;
            task.LastError ??= "Recovered expired running task.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
