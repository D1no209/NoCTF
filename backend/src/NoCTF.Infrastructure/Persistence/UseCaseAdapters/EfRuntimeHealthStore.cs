using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfRuntimeHealthStore(NoCtfDbContext db) : IRuntimeHealthStore
{
    public async Task<IReadOnlyList<RuntimeHealthTarget>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var records = await db.ChallengeInstances.AsNoTracking()
            .Where(instance => instance.Status != RuntimeStatus.Stopped && instance.Status != RuntimeStatus.Failed)
            .OrderBy(instance => instance.CreatedAt)
            .Select(instance => new { instance.Id, instance.Receipt })
            .ToListAsync(cancellationToken);
        return records.Select(record =>
        {
            var receipt = JsonSerializer.Deserialize<ContainerReceipt>(record.Receipt)
                ?? throw new InvalidOperationException($"Runtime receipt for instance {record.Id} is invalid.");
            return new RuntimeHealthTarget(record.Id, receipt.ResourceId, receipt);
        }).ToArray();
    }

    public Task UpdateStatusAsync(Guid instanceId, RuntimeStatus status, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.ChallengeInstances
            .Where(instance => instance.Id == instanceId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(instance => instance.Status, status)
                .SetProperty(instance => instance.UpdatedAt, now), cancellationToken);
}
