using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfRuntimeOperationStore(NoCtfDbContext db) : IRuntimeOperationStore
{
    public async Task<RuntimeOperationLease> BeginAsync(
        Guid competitionId,
        string operationKey,
        RuntimeOperationKind kind,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await db.RuntimeOperations.AsNoTracking().SingleOrDefaultAsync(
            operation => operation.CompetitionId == competitionId
                         && operation.OperationKey == operationKey,
            ct);
        if (existing is not null)
            return new(existing.Id, existing.CompetitionId, existing.OperationKey, existing.Status, false);

        var operationId = Guid.CreateVersion7(now);
        db.RuntimeOperations.Add(new RuntimeOperation
        {
            Id = operationId,
            CompetitionId = competitionId,
            OperationKey = operationKey,
            Kind = kind,
            Status = RuntimeStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return new(operationId, competitionId, operationKey, RuntimeStatus.Pending, true);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await db.RuntimeOperations.AsNoTracking().SingleAsync(
                operation => operation.CompetitionId == competitionId
                             && operation.OperationKey == operationKey,
                ct);
            return new(raced.Id, raced.CompetitionId, raced.OperationKey, raced.Status, false);
        }
    }

    public async Task<bool> CompleteAsync(
        RuntimeOperationLease lease,
        ContainerReceipt receipt,
        Guid challengeId,
        Guid? teamId,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var operation = await db.RuntimeOperations.SingleOrDefaultAsync(
            item => item.Id == lease.OperationId && item.OperationKey == lease.OperationKey,
            ct);
        if (operation is null || operation.Status is RuntimeStatus.Running or RuntimeStatus.Stopped)
            return false;

        var instance = new ChallengeInstance
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = lease.CompetitionId,
            ChallengeId = challengeId,
            TeamId = teamId,
            Provider = receipt.Provider,
            Receipt = JsonSerializer.Serialize(receipt),
            Status = receipt.Status,
            EntryUrl = receipt.PublicHost,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt
        };
        db.ChallengeInstances.Add(instance);
        operation.ChallengeInstanceId = instance.Id;
        operation.Status = receipt.Status;
        operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task FailAsync(
        RuntimeOperationLease lease,
        string errorCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var operation = await db.RuntimeOperations.SingleOrDefaultAsync(
            item => item.Id == lease.OperationId && item.OperationKey == lease.OperationKey,
            ct);
        if (operation is null)
            return;
        operation.Status = RuntimeStatus.Failed;
        operation.ErrorCode = errorCode;
        operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }
}
