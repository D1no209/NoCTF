using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>
/// A transaction opened only when it is first needed. Reads before <see cref="BeginAsync"/> do not pin a
/// pooled connection inside a transaction; the first call fixes the isolation level for the unit of work.
/// </summary>
internal sealed class LateTransaction(NoCtfDbContext db) : IAsyncDisposable
{
    private IDbContextTransaction? transaction;

    public async Task BeginAsync(IsolationLevel isolation, CancellationToken ct) =>
        transaction ??= await db.Database.BeginTransactionAsync(isolation, ct);

    public Task CommitAsync(CancellationToken ct) =>
        (transaction ?? throw new InvalidOperationException("The transaction has not been started.")).CommitAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (transaction is null) return;
        var current = transaction;
        transaction = null;
        await current.DisposeAsync();
    }
}
