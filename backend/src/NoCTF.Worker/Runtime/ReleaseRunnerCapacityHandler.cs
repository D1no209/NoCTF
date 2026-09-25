using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Worker;

public sealed class ReleaseRunnerCapacityHandler(
    NoCtfDbContext db, IRunnerCapacityGate developmentCapacity, IPostCommitMessagePublisher outbox, TimeProvider clock,
    RedisRunnerCapacityGate? capacity = null)
{
    public async Task Handle(ReleaseRunnerCapacity message, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await developmentCapacity.ReleaseWorkloadAsync(message.Identity, message.RunnerId, ct);
            await outbox.PublishAsync(new DispatchQueuedRuntimes(clock.GetUtcNow()));
            return;
        }
        if (capacity is null)
            throw new InvalidOperationException("A durable capacity release requires the Redis ledger.");
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Include(x => x.CapacityAllocationEntries)
            .SingleOrDefaultAsync(x => x.Id == message.Identity.RuntimeInstanceId, ct);
        var document = runtime?.CapacityAllocations;
        if (document?.Items.Any(item => item.Identity == message.Identity) == true)
            return;
        var result = await capacity.ReleaseWorkloadAsync(message.Identity, message.RunnerId, ct);
        if (result is RunnerCapacityReleaseOutcome.RecoveryRequired or RunnerCapacityReleaseOutcome.OwnerMismatch)
            throw new InvalidOperationException("Capacity release requires ledger reconciliation.");
        await outbox.PublishAsync(new DispatchQueuedRuntimes(clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
        }
    }
}
