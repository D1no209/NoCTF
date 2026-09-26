using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class ReleaseRunnerCapacityHandler(
    NoCtfDbContext db, IRunnerCapacityGate developmentCapacity,
    IPostCommitMessagePublisher outbox, TimeProvider clock)
{
    public async Task Handle(ReleaseRunnerCapacity message, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await developmentCapacity.ReleaseWorkloadAsync(message.Identity, message.RunnerId, ct);
            await outbox.PublishAsync(new DispatchQueuedRuntimes(clock.GetUtcNow()));
            return;
        }
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Include(x => x.CapacityAllocationEntries)
            .SingleOrDefaultAsync(x => x.Id == message.Identity.RuntimeInstanceId, ct);
        var document = runtime?.CapacityAllocations;
        if (document?.Items.Any(item => item.Identity == message.Identity) == true)
            return;
        await outbox.PublishAsync(new DispatchQueuedRuntimes(clock.GetUtcNow()));
        await outbox.FlushCommittedMessagesAsync();
    }
}
