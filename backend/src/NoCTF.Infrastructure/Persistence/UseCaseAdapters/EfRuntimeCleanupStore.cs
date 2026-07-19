using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfRuntimeCleanupStore(NoCtfDbContext db) : IRuntimeCleanupStore
{
    public async Task<IReadOnlyList<RuntimeCleanupTarget>> ListActiveAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var instances = await db.ChallengeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId
                               && instance.Status != RuntimeStatus.Stopped)
            .OrderBy(instance => instance.CreatedAt)
            .ThenBy(instance => instance.Id)
            .Select(instance => new { instance.Id, instance.Receipt })
            .ToListAsync(cancellationToken);
        return instances.Select(instance => new RuntimeCleanupTarget(
            instance.Id,
            JsonSerializer.Deserialize<ContainerReceipt>(instance.Receipt)
            ?? throw new InvalidOperationException($"Runtime receipt for instance {instance.Id} is invalid.")))
            .ToArray();
    }

    public async Task MarkStoppedAsync(
        IReadOnlyCollection<Guid> instanceIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.ChallengeInstances
            .Where(instance => instanceIds.Contains(instance.Id))
            .ExecuteUpdateAsync(update => update
                .SetProperty(instance => instance.Status, RuntimeStatus.Stopped)
                .SetProperty(instance => instance.UpdatedAt, now), cancellationToken);
    }
}
