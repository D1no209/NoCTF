using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfRuntimeOperationStore(NoCtfDbContext db) : IRuntimeOperationStore
{
    public async Task<RuntimeOperationBeginResult> BeginAsync(
        Guid competitionId,
        string operationKey,
        RuntimeOperationKind kind,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        var existing = await db.RuntimeOperations.AsNoTracking().SingleOrDefaultAsync(
            operation => operation.CompetitionId == competitionId
                         && operation.OperationKey == operationKey,
            ct);
        if (existing is not null)
        {
            var reclaimable = existing.Status == RuntimeStatus.Failed
                              || existing.Status == RuntimeStatus.Starting && existing.UpdatedAt <= staleBefore;
            if (!reclaimable)
                return new(new(existing.Id, existing.CompetitionId, existing.OperationKey, existing.ClaimToken, existing.Status, false));
            if (competitionStatus != CompetitionStatus.Running)
                return new(null, RuntimeOperationBeginFailure.CompetitionNotRunning);
            if (existing.ChallengeInstanceId is { } staleInstanceId)
            {
                await db.ChallengeInstances
                    .Where(instance => instance.Id == staleInstanceId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(instance => instance.Status, RuntimeStatus.Failed)
                        .SetProperty(instance => instance.UpdatedAt, now), ct);
                await db.ChallengeFlags
                    .Where(flag => flag.ChallengeInstanceId == staleInstanceId
                                   && (flag.ValidEnd == null || flag.ValidEnd > now))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(flag => flag.ValidEnd, now)
                        .SetProperty(flag => flag.UpdatedAt, now)
                        .SetProperty(flag => flag.RowVersion, flag => flag.RowVersion + 1), ct);
            }
            var newClaimToken = Guid.CreateVersion7(now);
            var claimed = await db.RuntimeOperations
                .Where(operation => operation.Id == existing.Id
                                    && operation.ClaimToken == existing.ClaimToken
                                    && (operation.Status == RuntimeStatus.Failed
                                        || operation.Status == RuntimeStatus.Starting
                                        && operation.UpdatedAt <= staleBefore))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(operation => operation.Status, RuntimeStatus.Starting)
                    .SetProperty(operation => operation.ClaimToken, newClaimToken)
                    .SetProperty(operation => operation.ChallengeInstanceId, (Guid?)null)
                    .SetProperty(operation => operation.ErrorCode, (string?)null)
                    .SetProperty(operation => operation.UpdatedAt, now), ct);
            if (claimed != 1)
                return new(new(existing.Id, existing.CompetitionId, existing.OperationKey, existing.ClaimToken, existing.Status, false));
            await transaction.CommitAsync(ct);
            return new(new(existing.Id, existing.CompetitionId, existing.OperationKey, newClaimToken, RuntimeStatus.Starting, true));
        }
        if (competitionStatus != CompetitionStatus.Running)
            return new(null, RuntimeOperationBeginFailure.CompetitionNotRunning);

        var operationId = Guid.CreateVersion7(now);
        var claimToken = Guid.CreateVersion7(now.AddTicks(1));
        db.RuntimeOperations.Add(new RuntimeOperation
        {
            Id = operationId,
            CompetitionId = competitionId,
            OperationKey = operationKey,
            Kind = kind,
            Status = RuntimeStatus.Starting,
            ClaimToken = claimToken,
            CreatedAt = now,
            UpdatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(new(operationId, competitionId, operationKey, claimToken, RuntimeStatus.Starting, true));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_runtime_operations_CompetitionId_OperationKey"
        })
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var raced = await db.RuntimeOperations.AsNoTracking().SingleAsync(
                operation => operation.CompetitionId == competitionId
                             && operation.OperationKey == operationKey,
                ct);
            return new(new(raced.Id, raced.CompetitionId, raced.OperationKey, raced.ClaimToken, raced.Status, false));
        }
    }

    public async Task<bool> CompleteAsync(
        RuntimeOperationLease lease,
        ContainerReceipt receipt,
        Guid challengeId,
        Guid? teamId,
        Guid challengeInstanceId,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, lease.CompetitionId, ct);
        if (competitionStatus != CompetitionStatus.Running)
            return false;
        var operation = await db.RuntimeOperations.SingleOrDefaultAsync(
            item => item.Id == lease.OperationId
                    && item.OperationKey == lease.OperationKey
                    && item.ClaimToken == lease.ClaimToken,
            ct);
        if (operation is null || operation.Status is RuntimeStatus.Running or RuntimeStatus.Stopped)
            return false;

        var instance = await db.ChallengeInstances.SingleOrDefaultAsync(
            item => item.Id == challengeInstanceId, ct);
        if (instance is null)
        {
            instance = new ChallengeInstance
            {
                Id = challengeInstanceId,
                CompetitionId = lease.CompetitionId,
                ChallengeId = challengeId,
                TeamId = teamId,
                CreatedAt = now
            };
            db.ChallengeInstances.Add(instance);
        }
        else if (instance.CompetitionId != lease.CompetitionId
                 || instance.ChallengeId != challengeId || instance.TeamId != teamId)
        {
            return false;
        }
        instance.Provider = receipt.Provider;
        instance.Receipt = JsonSerializer.Serialize(receipt);
        instance.Status = receipt.Status;
        instance.EntryUrl = receipt.PublicHost;
        instance.UpdatedAt = now;
        instance.ExpiresAt = expiresAt;
        operation.ChallengeInstanceId = instance.Id;
        operation.Status = receipt.Status;
        operation.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<RuntimeOperationFailureResult> FailAsync(
        RuntimeOperationLease lease,
        RuntimeOperationFailureContext failure,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        _ = await CompetitionWriteLock.AcquireAsync(db, lease.CompetitionId, ct);
        var operation = await db.RuntimeOperations.SingleOrDefaultAsync(
            item => item.Id == lease.OperationId && item.OperationKey == lease.OperationKey,
            ct);
        if (operation is null)
            return new();
        if (operation.ClaimToken != lease.ClaimToken)
        {
            if (failure.CleanupReceipt is { } staleReceipt)
                db.ChallengeInstances.Add(CreateCleanupInstance(lease, failure, staleReceipt, now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new();
        }
        if (operation.ChallengeInstanceId is not null)
        {
            var reconciledInstance = await db.ChallengeInstances
                .Where(instance => instance.Id == operation.ChallengeInstanceId)
                .SingleOrDefaultAsync(ct);
            var reconciledReceipt = string.IsNullOrWhiteSpace(reconciledInstance?.Receipt)
                ? null
                : JsonSerializer.Deserialize<ContainerReceipt>(reconciledInstance.Receipt)
                  ?? throw new InvalidOperationException("Persisted runtime receipt is invalid.");
            if (reconciledInstance is not null && reconciledReceipt is null)
            {
                reconciledInstance.Status = RuntimeStatus.Failed;
                reconciledInstance.UpdatedAt = now;
                operation.Status = RuntimeStatus.Failed;
                operation.ErrorCode = FailureCode(failure.Failure);
                operation.UpdatedAt = now;
                if (failure.CleanupReceipt is { } preparedCleanupReceipt)
                {
                    reconciledInstance.Provider = preparedCleanupReceipt.Provider;
                    reconciledInstance.Receipt = JsonSerializer.Serialize(preparedCleanupReceipt);
                }
            }
            if (failure.CleanupReceipt is { } cleanupReceipt
                && (reconciledInstance is null
                    || reconciledReceipt is not null
                    && (reconciledReceipt.Provider != cleanupReceipt.Provider
                        || reconciledReceipt.ResourceId != cleanupReceipt.ResourceId)))
                db.ChallengeInstances.Add(CreateCleanupInstance(lease, failure, cleanupReceipt, now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(reconciledReceipt);
        }
        operation.Status = RuntimeStatus.Failed;
        operation.ErrorCode = FailureCode(failure.Failure);
        operation.UpdatedAt = now;
        if (failure.CleanupReceipt is { } receipt && operation.ChallengeInstanceId is null)
        {
            var instance = CreateCleanupInstance(lease, failure, receipt, now);
            db.ChallengeInstances.Add(instance);
            operation.ChallengeInstanceId = instance.Id;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new();
    }

    private static string FailureCode(RuntimeProvisionFailure failure) => failure switch
        {
            RuntimeProvisionFailure.CreateTimeout => "runtime_create_timeout",
            RuntimeProvisionFailure.CreateFailed => "runtime_create_failed",
            RuntimeProvisionFailure.CreateCanceled => "runtime_create_canceled",
            RuntimeProvisionFailure.ReceiptMismatch => "runtime_receipt_mismatch",
            RuntimeProvisionFailure.CompetitionNotRunning => "competition_not_running",
            RuntimeProvisionFailure.PersistenceRejected => "runtime_persistence_rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };

    private static ChallengeInstance CreateCleanupInstance(
        RuntimeOperationLease lease,
        RuntimeOperationFailureContext failure,
        ContainerReceipt receipt,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = lease.CompetitionId,
            ChallengeId = failure.ChallengeId,
            TeamId = failure.TeamId,
            Provider = receipt.Provider,
            Receipt = JsonSerializer.Serialize(receipt),
            Status = RuntimeStatus.Failed,
            EntryUrl = receipt.PublicHost,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = failure.ExpiresAt
        };
}
