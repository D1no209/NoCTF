using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NoCTF.Application.Common;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Persistence;

internal sealed class AggregatePatchTransaction(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    AggregatePatchPostCommitActions? postCommitActions = null) : IAtomicAggregatePatch
{
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<AtomicAggregatePatchDecision<TResult>>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
        {
            var inMemoryDecision = await operation(cancellationToken);
            if (!inMemoryDecision.ShouldCommit)
                db.ChangeTracker.Clear();
            return inMemoryDecision.Result;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        postCommitActions?.Begin();
        var committed = false;
        try
        {
            var decision = await operation(cancellationToken);
            if (!decision.ShouldCommit)
            {
                await transaction.RollbackAsync(cancellationToken);
                postCommitActions?.Discard();
                db.ChangeTracker.Clear();
                return decision.Result;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            if (postCommitActions is not null)
                await postCommitActions.CompleteAsync(CancellationToken.None);
            await outbox.FlushCommittedMessagesAsync();
            return decision.Result;
        }
        catch
        {
            postCommitActions?.Discard();
            if (!committed)
                await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}

/// <summary>
/// Owns a local transaction only when the caller did not already establish the
/// aggregate PATCH transaction. Commit and rollback are deliberately no-ops for
/// the ambient owner.
/// </summary>
internal sealed class AggregateCompatibleTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction? transaction;

    private AggregateCompatibleTransaction(IDbContextTransaction? transaction) =>
        this.transaction = transaction;

    public static async Task<AggregateCompatibleTransaction> BeginAsync(
        NoCtfDbContext db,
        CancellationToken cancellationToken) =>
        new(db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null);

    public static async Task<AggregateCompatibleTransaction> BeginAsync(
        NoCtfDbContext db,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken) =>
        new(db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(isolationLevel, cancellationToken)
            : null);

    public Task CommitAsync(CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken) =>
        transaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() =>
        transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
