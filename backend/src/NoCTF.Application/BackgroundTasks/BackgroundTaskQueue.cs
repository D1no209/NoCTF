using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    Task<BackgroundTaskItem?> TryAcquireNextAsync(
        TimeSpan lockDuration,
        IReadOnlyCollection<string>? includedTypes,
        IReadOnlyCollection<string>? excludedTypes,
        CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(Guid taskId, string lockOwner, TimeSpan lockDuration, CancellationToken cancellationToken = default);
    Task MarkSucceededAsync(Guid taskId, string lockOwner, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid taskId, string lockOwner, Exception exception, CancellationToken cancellationToken = default);
    Task<int> RecoverExpiredRunningTasksAsync(TimeSpan lockTimeout, CancellationToken cancellationToken = default);
}

public class BackgroundTaskQueue(ApplicationDbContext dbContext) : IBackgroundTaskQueue
{
    internal const int RecoveryBatchSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> EnqueueAsync<TPayload>(
        Guid competitionId,
        string type,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        type = type.Trim();
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

    public Task<BackgroundTaskItem?> TryAcquireNextAsync(
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
        => TryAcquireNextAsync(lockDuration, null, null, cancellationToken);

    public async Task<BackgroundTaskItem?> TryAcquireNextAsync(
        TimeSpan lockDuration,
        IReadOnlyCollection<string>? includedTypes,
        IReadOnlyCollection<string>? excludedTypes,
        CancellationToken cancellationToken = default)
    {
        if (lockDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockDuration), "The task lease duration must be positive.");

        var now = DateTime.UtcNow;
        var lockOwner = Guid.NewGuid().ToString("N");
        var included = includedTypes?
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var excluded = excludedTypes?
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (included.Length > 0 && excluded.Length > 0 && included.Intersect(excluded, StringComparer.Ordinal).Any())
            throw new ArgumentException("A background task type cannot be both included and excluded.");

        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await TryAcquirePostgresAsync(
                now,
                lockOwner,
                lockDuration,
                included,
                excluded,
                cancellationToken);
        }

        if (dbContext.Database.IsRelational())
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var candidates = ApplyTypeFilter(
                    dbContext.BackgroundTasks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(t =>
                        (t.Status == BackgroundTaskStatus.Pending || t.Status == BackgroundTaskStatus.Retrying) &&
                        (t.LockedUntil == null || t.LockedUntil < now)),
                    included,
                    excluded);
                var candidateId = await candidates
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
                        .AsNoTracking()
                        .FirstAsync(t => t.Id == candidateId.Value, cancellationToken);
                }
            }

            return null;
        }

        var pending = ApplyTypeFilter(
            dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                (t.Status == BackgroundTaskStatus.Pending || t.Status == BackgroundTaskStatus.Retrying) &&
                (t.LockedUntil == null || t.LockedUntil < now)),
            included,
            excluded);
        var task = await pending
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

    private async Task<BackgroundTaskItem?> TryAcquirePostgresAsync(
        DateTime now,
        string lockOwner,
        TimeSpan lockDuration,
        string[] includedTypes,
        string[] excludedTypes,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        BackgroundTaskItem? claimedTask = null;
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var commandTimeout = dbContext.Database.GetCommandTimeout();
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            var sql = new StringBuilder(
                """
                WITH candidate AS (
                    SELECT "Id"
                    FROM "BackgroundTasks"
                    WHERE "Status" IN (@pending_status, @retrying_status)
                      AND ("LockedUntil" IS NULL OR "LockedUntil" < @now)
                """);
            AppendTypeFilter(sql, command, "included_type", includedTypes, exclude: false);
            AppendTypeFilter(sql, command, "excluded_type", excludedTypes, exclude: true);
            sql.Append(
                """

                    ORDER BY "CreatedAt", "Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE "BackgroundTasks" AS task
                SET "Status" = @running_status,
                    "AttemptCount" = task."AttemptCount" + 1,
                    "LockOwner" = @lock_owner,
                    "LockedUntil" = @locked_until,
                    "UpdatedAt" = @now
                FROM candidate
                WHERE task."Id" = candidate."Id"
                RETURNING task."Id",
                          task."CompetitionId",
                          task."Type",
                          task."Status",
                          task."PayloadJson",
                          task."AttemptCount",
                          task."MaxAttempts",
                          task."LastError",
                          task."CreatedAt",
                          task."UpdatedAt",
                          task."LockedUntil",
                          task."LockOwner"
                """);
            command.CommandText = sql.ToString();
            AddParameter(command, "pending_status", (int)BackgroundTaskStatus.Pending);
            AddParameter(command, "retrying_status", (int)BackgroundTaskStatus.Retrying);
            AddParameter(command, "running_status", (int)BackgroundTaskStatus.Running);
            AddParameter(command, "now", now);
            AddParameter(command, "lock_owner", lockOwner);
            AddParameter(command, "locked_until", now.Add(lockDuration));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                claimedTask = new BackgroundTaskItem
                {
                    Id = reader.GetGuid(0),
                    CompetitionId = reader.GetGuid(1),
                    Type = reader.GetString(2),
                    Status = (BackgroundTaskStatus)reader.GetInt32(3),
                    PayloadJson = reader.GetString(4),
                    AttemptCount = reader.GetInt32(5),
                    MaxAttempts = reader.GetInt32(6),
                    LastError = reader.IsDBNull(7) ? null : reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8),
                    UpdatedAt = reader.GetDateTime(9),
                    LockedUntil = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    LockOwner = reader.IsDBNull(11) ? null : reader.GetString(11)
                };
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        return claimedTask;
    }

    private static void AppendTypeFilter(
        StringBuilder sql,
        DbCommand command,
        string parameterPrefix,
        string[] taskTypes,
        bool exclude)
    {
        if (taskTypes.Length == 0)
            return;

        var parameterNames = new string[taskTypes.Length];
        for (var index = 0; index < taskTypes.Length; index++)
        {
            var parameterName = $"{parameterPrefix}_{index}";
            parameterNames[index] = $"@{parameterName}";
            AddParameter(command, parameterName, taskTypes[index]);
        }

        sql.Append(exclude ? "  AND \"Type\" NOT IN (" : "  AND \"Type\" IN (")
            .AppendJoin(", ", parameterNames)
            .AppendLine(")");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static IQueryable<BackgroundTaskItem> ApplyTypeFilter(
        IQueryable<BackgroundTaskItem> query,
        string[] includedTypes,
        string[] excludedTypes)
    {
        if (includedTypes.Length > 0)
            query = query.Where(task => includedTypes.Contains(task.Type));
        if (excludedTypes.Length > 0)
            query = query.Where(task => !excludedTypes.Contains(task.Type));
        return query;
    }

    public async Task<bool> RenewLeaseAsync(
        Guid taskId,
        string lockOwner,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        if (lockDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockDuration), "The task lease duration must be positive.");

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
        if (dbContext.Database.IsRelational())
        {
            var now = DateTime.UtcNow;
            var updated = await dbContext.BackgroundTasks
                .IgnoreQueryFilters()
                .Where(task =>
                    task.Id == taskId &&
                    task.Status == BackgroundTaskStatus.Running &&
                    task.LockOwner == lockOwner)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(task => task.Status, BackgroundTaskStatus.Succeeded)
                    .SetProperty(task => task.LockedUntil, (DateTime?)null)
                    .SetProperty(task => task.LockOwner, (string?)null)
                    .SetProperty(task => task.UpdatedAt, now), cancellationToken);
            if (updated != 1)
                throw new InvalidOperationException($"Background task {taskId} lease was lost before completion.");
            return;
        }

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
        if (dbContext.Database.IsRelational())
        {
            var now = DateTime.UtcNow;
            var error = SanitizeError(exception);
            var updated = await dbContext.BackgroundTasks
                .IgnoreQueryFilters()
                .Where(task =>
                    task.Id == taskId &&
                    task.Status == BackgroundTaskStatus.Running &&
                    task.LockOwner == lockOwner)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        task => task.Status,
                        task => task.AttemptCount < task.MaxAttempts
                            ? BackgroundTaskStatus.Retrying
                            : BackgroundTaskStatus.Failed)
                    .SetProperty(
                        task => task.LockedUntil,
                        task => task.AttemptCount < task.MaxAttempts
                            ? now.AddSeconds(Math.Min(300, 10 * task.AttemptCount))
                            : (DateTime?)null)
                    .SetProperty(task => task.LockOwner, (string?)null)
                    .SetProperty(task => task.LastError, error)
                    .SetProperty(task => task.UpdatedAt, now), cancellationToken);
            if (updated != 1)
                throw new InvalidOperationException($"Background task {taskId} lease was lost before failure handling.");
            return;
        }

        var task = await dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId && t.LockOwner == lockOwner, cancellationToken);

        task.LastError = SanitizeError(exception);
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
        if (lockTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockTimeout), "The task recovery timeout must be positive.");

        var now = DateTime.UtcNow;
        var expiredBefore = now.Subtract(lockTimeout);
        var expiredQuery = dbContext.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                t.Status == BackgroundTaskStatus.Running &&
                (t.LockedUntil == null || t.LockedUntil < now || t.UpdatedAt < expiredBefore))
            .OrderBy(t => t.UpdatedAt)
            .Take(RecoveryBatchSize);

        if (dbContext.Database.IsRelational())
        {
            var candidateIds = await expiredQuery
                .AsNoTracking()
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            if (candidateIds.Count == 0)
                return 0;

            // Re-check the lease predicate in the UPDATE. A worker that renews or completes
            // after candidate selection no longer matches and keeps ownership of its task.
            return await dbContext.BackgroundTasks
                .IgnoreQueryFilters()
                .Where(t =>
                    candidateIds.Contains(t.Id) &&
                    t.Status == BackgroundTaskStatus.Running &&
                    (t.LockedUntil == null || t.LockedUntil < now || t.UpdatedAt < expiredBefore))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        t => t.Status,
                        t => t.AttemptCount < t.MaxAttempts
                            ? BackgroundTaskStatus.Retrying
                            : BackgroundTaskStatus.Failed)
                    .SetProperty(t => t.LockedUntil, (DateTime?)null)
                    .SetProperty(t => t.LockOwner, (string?)null)
                    .SetProperty(t => t.UpdatedAt, now)
                    .SetProperty(
                        t => t.LastError,
                        t => t.LastError ?? "Recovered expired running task."),
                    cancellationToken);
        }

        var expired = await expiredQuery.ToListAsync(cancellationToken);
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

    private static string SanitizeError(Exception exception)
    {
        var message = exception.Message;
        string[] sensitiveMarkers = ["password", "secret", "token", "flag{", "connectionstring"];
        if (sensitiveMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return $"{exception.GetType().Name}: [REDACTED]";

        const int maxLength = 1_024;
        return message.Length <= maxLength ? message : message[..maxLength];
    }
}
