using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Worker;

public sealed class ReleaseRunnerCapacityHandler(
    NoCtfDbContext db, RedisRunnerCapacityGate capacity, ITransactionalMessageOutbox outbox, TimeProvider clock)
{
    public async Task Handle(ReleaseRunnerCapacity message, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await RuntimeCapacityCriticalSection.AcquireAsync(db, ct);
        var document = await db.RuntimeInstances.AsNoTracking().Where(x => x.Id == message.Identity.RuntimeInstanceId)
            .Select(x => x.CapacityAllocations).SingleOrDefaultAsync(ct);
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
