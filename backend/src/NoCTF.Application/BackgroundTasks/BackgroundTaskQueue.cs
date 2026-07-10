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

    Task<bool> RenewLeaseAsync(Guid taskId, string lockOwner, TimeSpan lockDuration, CancellationToken cancellationToken = default);
    Task MarkSucceededAsync(Guid taskId, string lockOwner, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid taskId, string lockOwner, Exception exception, CancellationToken cancellationToken = default);
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
        var lockOwner = Guid.NewGuid().ToString("N");
        if (dbContext.Database.IsRelational())
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var candidateId = await dbContext.BackgroundTasks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(t =>
                        (t.Status == BackgroundTaskStatus.Pending || t.Status == BackgroundTaskStatus.Retrying) &&
                        (t.LockedUntil == null || t.LockedUntil < now))
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => (Guid?)t.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (candidateId is null)
                    return null;

                var updated = await dbContext.BackgroundTasks
                    .IgnoreQueryFilters()
                    .Where(t =>
                        t.Id == candidateId.Value &&
                        (t.Status == BackgroundTaskStatus.Pending || t.Status == BackgroundTaskStatus.Retrying) &&
                        (t.LockedUntil == null || t.LockedUntil < now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Status, BackgroundTaskStatus.Running)
                        .SetProperty(t => t.AttemptCount, t => t.AttemptCount + 1)
                        .SetProperty(t => t.LockOwner, lockOwner)
                        .SetProperty(t => t.LockedUntil, now.Add(lockDuration))
                        .SetProperty(t => t.UpdatedAt, now), cancellationToken);

                if (updated == 1)
                {
                    return await dbContext.BackgroundTasks
                        .IgnoreQueryFilters()
                        .FirstAsync(t => t.Id == candidateId.Value, cancellationToken);
                }
            }

            return null;
        }

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
        task.LockOwner = lockOwner;
        task.LockedUntil = now.Add(lockDuration);
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<bool> RenewLeaseAsync(
        Guid taskId,
        string lockOwner,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (!dbContext.Database.IsRelational())
        {
            var task = await dbContext.BackgroundTasks
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t =>
                    t.Id == taskId &&
                    t.Status == BackgroundTaskStatus.Running &&
                    t.LockOwner == lockOwner, cancellationToken);
            if (task is null)
                return false;
            task.LockedUntil = now.Add(lockDuration);
            task.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                t.Id == taskId &&
                t.Status == BackgroundTaskStatus.Running &&
                t.LockOwner == lockOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.LockedUntil, now.Add(lockDuration))
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
        return updated == 1;
    }

    public async Task MarkSucceededAsync(Guid taskId, string lockOwner, CancellationToken cancellationToken = default)
    {
        var task = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId && t.LockOwner == lockOwner, cancellationToken);

        task.Status = BackgroundTaskStatus.Succeeded;
        task.LockedUntil = null;
        task.LockOwner = null;
        task.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid taskId,
        string lockOwner,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        var task = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId && t.LockOwner == lockOwner, cancellationToken);

        task.LastError = exception.Message;
        task.Status = task.AttemptCount < task.MaxAttempts
            ? BackgroundTaskStatus.Retrying
            : BackgroundTaskStatus.Failed;
        task.LockedUntil = task.Status == BackgroundTaskStatus.Retrying
            ? DateTime.UtcNow.AddSeconds(Math.Min(300, 10 * task.AttemptCount))
            : null;
        task.LockOwner = null;
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
            task.LockOwner = null;
            task.UpdatedAt = now;
            task.LastError ??= "Recovered expired running task.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
