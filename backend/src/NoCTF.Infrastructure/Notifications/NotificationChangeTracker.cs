using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

/// <summary>
/// Sends cache-invalidation hints only after an implicit save transaction or an
/// explicit EF transaction has committed. The notification rows remain authoritative.
/// </summary>
internal sealed class NotificationChangeTracker(
    NoCtfDbContext db,
    INotificationChangePublisher publisher,
    ILogger<NoCtfDbContext>? logger)
{
    private readonly HashSet<NotificationAudience> currentSave = [];
    private readonly HashSet<NotificationAudience> committedSaves = [];

    public SaveInterceptor Saves { get; } = new();
    public TransactionInterceptor Transactions { get; } = new();

    public void Capture()
    {
        currentSave.Clear();
        foreach (var entry in db.ChangeTracker.Entries<Notification>()
                     .Where(entry => entry.State == EntityState.Added))
            currentSave.Add(new(entry.Entity.TargetType, entry.Entity.TargetId,
                entry.Entity.ThreadRootId));
    }

    public async Task SavedAsync()
    {
        committedSaves.UnionWith(currentSave);
        currentSave.Clear();
        if (db.Database.CurrentTransaction is null)
            await FlushAsync();
    }

    public void FailedSave() => currentSave.Clear();

    public void Discard()
    {
        currentSave.Clear();
        committedSaves.Clear();
    }

    public async Task FlushAsync()
    {
        if (committedSaves.Count == 0) return;
        var audiences = committedSaves.ToArray();
        committedSaves.Clear();
        try
        {
            await publisher.PublishAsync(new(audiences), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception,
                "Notification committed; realtime invalidation will be recovered by client refresh.");
        }
    }

    public sealed class SaveInterceptor : SaveChangesInterceptor
    {
        internal NotificationChangeTracker Owner { get; set; } = null!;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Owner.Capture();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Owner.Capture();
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Owner.SavedAsync().GetAwaiter().GetResult();
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            await Owner.SavedAsync();
            return result;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
            Owner.FailedSave();

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Owner.FailedSave();
            return Task.CompletedTask;
        }
    }

    public sealed class TransactionInterceptor : DbTransactionInterceptor
    {
        internal NotificationChangeTracker Owner { get; set; } = null!;

        public override void TransactionCommitted(
            DbTransaction transaction, TransactionEndEventData eventData) =>
            Owner.FlushAsync().GetAwaiter().GetResult();

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => Owner.FlushAsync();

        public override void TransactionRolledBack(
            DbTransaction transaction, TransactionEndEventData eventData) => Owner.Discard();

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Owner.Discard();
            return Task.CompletedTask;
        }

        public override void TransactionFailed(
            DbTransaction transaction, TransactionErrorEventData eventData) => Owner.Discard();

        public override Task TransactionFailedAsync(
            DbTransaction transaction,
            TransactionErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Owner.Discard();
            return Task.CompletedTask;
        }
    }

    public void Attach()
    {
        Saves.Owner = this;
        Transactions.Owner = this;
    }
}
