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
            .Where(instance => instance.Status != RuntimeStatus.Stopped
                               && instance.Status != RuntimeStatus.Failed
                               && instance.Receipt != string.Empty)
            .OrderBy(instance => instance.CreatedAt)
            .Select(instance => new { instance.Id, instance.Receipt, instance.Status })
            .ToListAsync(cancellationToken);
        return records.Select(record =>
        {
            var receipt = JsonSerializer.Deserialize<ContainerReceipt>(record.Receipt)
                ?? throw new InvalidOperationException($"Runtime receipt for instance {record.Id} is invalid.");
            return new RuntimeHealthTarget(record.Id, receipt.ResourceId, receipt, record.Status);
        }).ToArray();
    }

    public async Task<bool> UpdateStatusAsync(
        Guid instanceId,
        RuntimeStatus expectedStatus,
        RuntimeStatus status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var competitionId = await db.ChallengeInstances
            .Where(instance => instance.Id == instanceId)
            .Select(instance => (Guid?)instance.CompetitionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (competitionId is null) return false;
        _ = await CompetitionWriteLock.AcquireAsync(db, competitionId.Value, cancellationToken);
        var instanceUpdated = await db.ChallengeInstances
            .Where(instance => instance.Id == instanceId
                               && instance.Status == expectedStatus)
            .ExecuteUpdateAsync(update => update
                .SetProperty(instance => instance.Status, status)
                .SetProperty(instance => instance.UpdatedAt, now), cancellationToken);
        if (instanceUpdated != 1) return false;
        var operationUpdated = await db.RuntimeOperations
            .Where(operation => operation.ChallengeInstanceId == instanceId
                                && operation.Status == expectedStatus)
            .ExecuteUpdateAsync(update => update
                .SetProperty(operation => operation.Status, status)
                .SetProperty(operation => operation.UpdatedAt, now), cancellationToken);
        if (operationUpdated != 1) return false;
        if (status == RuntimeStatus.Failed)
            await RuntimeFailurePersistence.InvalidateFlagsAsync(db, instanceId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
