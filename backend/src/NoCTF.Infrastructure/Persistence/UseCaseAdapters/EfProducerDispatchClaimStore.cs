using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfProducerDispatchClaimStore(NoCtfDbContext db) : IProducerDispatchClaimStore
{
    public async Task<ProducerDispatchClaim?> TryBeginAsync(
        Guid competitionId,
        string operationKey,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, cancellationToken) != CompetitionStatus.Running)
            return null;
        var existing = await db.RuntimeOperations.SingleOrDefaultAsync(item =>
            item.CompetitionId == competitionId && item.OperationKey == operationKey, cancellationToken);
        if (await db.ScoringEvents.AsNoTracking().AnyAsync(item =>
                item.CompetitionId == competitionId
                && item.Kind == ScoringEventKind.AwdServiceCheck
                && item.SourceKey == operationKey, cancellationToken))
        {
            if (existing is not null && existing.Status is RuntimeStatus.Starting or RuntimeStatus.Failed)
            {
                existing.Status = RuntimeStatus.Stopped;
                existing.ErrorCode = null;
                existing.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if (existing is not null)
        {
            if (existing.Status != RuntimeStatus.Failed
                && (existing.Status != RuntimeStatus.Starting || existing.UpdatedAt > staleBefore))
                return null;
            existing.Status = RuntimeStatus.Starting;
            existing.ClaimToken = Guid.CreateVersion7(now);
            existing.ErrorCode = null;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(existing.Id, existing.ClaimToken);
        }

        var entity = new RuntimeOperation
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = competitionId,
            OperationKey = operationKey,
            Kind = RuntimeOperationKind.OneShot,
            Status = RuntimeStatus.Starting,
            ClaimToken = Guid.CreateVersion7(now.AddTicks(1)),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.RuntimeOperations.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(entity.Id, entity.ClaimToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_runtime_operations_CompetitionId_OperationKey"
        })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<bool> CompleteAsync(
        Guid competitionId,
        string operationKey,
        ProducerDispatchClaim claim,
        bool succeeded,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, cancellationToken) is null)
            return false;
        var callbackCommitted = !succeeded && await db.ScoringEvents.AsNoTracking().AnyAsync(item =>
            item.CompetitionId == competitionId
            && item.Kind == ScoringEventKind.AwdServiceCheck
            && item.SourceKey == operationKey, cancellationToken);
        var completed = succeeded || callbackCommitted;
        var updated = await db.RuntimeOperations
            .Where(item => item.Id == claim.OperationId
                           && item.CompetitionId == competitionId
                           && item.OperationKey == operationKey
                           && item.ClaimToken == claim.ClaimToken
                           && item.Status == RuntimeStatus.Starting)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, completed ? RuntimeStatus.Stopped : RuntimeStatus.Failed)
                .SetProperty(item => item.ErrorCode, completed ? null : "producer_dispatch_failed")
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated == 1;
    }
}
