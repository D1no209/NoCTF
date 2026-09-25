using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Worker;

public sealed class QueuedRuntimeDispatchHandler(NoCtfDbContext db, IPostCommitMessagePublisher outbox,
    RuntimeDispatchWakeupGate wakeups,
    ILogger<QueuedRuntimeDispatchHandler> logger,
    TimeProvider? clock = null)
{
    public const int BatchSize = 64;

    public async Task Handle(DispatchQueuedRuntimes message, CancellationToken ct)
    {
        if (message.AfterId is null && !await wakeups.TryBeginAsync(ct))
            return;
        var scanAt = message.AfterId is null
            ? (clock ?? TimeProvider.System).GetUtcNow()
            : message.At;
        var query = db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.State == RuntimeState.Queued
                || runtime.State == RuntimeState.Provisioning);
        if (message.AfterCreatedAt is { } after && message.AfterId is { } id)
            query = query.Where(runtime => runtime.CreatedAt > after
                || runtime.CreatedAt == after && runtime.Id.CompareTo(id) > 0);
        var batch = await query.OrderBy(runtime => runtime.CreatedAt).ThenBy(runtime => runtime.Id)
            .Take(BatchSize).Select(runtime => new { runtime.Id, runtime.CreatedAt }).ToArrayAsync(ct);
        logger.LogDebug(
            "Queued Runtime scan scheduled for {ScheduledAt}, processed at {ScanAt}, selected {Count} instances.",
            message.At,
            scanAt,
            batch.Length);
        foreach (var runtime in batch)
            await outbox.PublishAsync(new DispatchRuntime(
                runtime.Id,
                Guid.CreateVersion7(scanAt)));
        if (batch.Length == BatchSize)
            await outbox.PublishAsync(new DispatchQueuedRuntimes(scanAt, batch[^1].CreatedAt, batch[^1].Id));
        // Messages must be persisted even when this scan does not modify a business row.
        await db.SaveChangesAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
    }
}
