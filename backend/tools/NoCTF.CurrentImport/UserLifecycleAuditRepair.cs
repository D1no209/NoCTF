using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.CurrentImport;

// Explicit import repair only: correct the column representation without changing the recorded action.
// Normal application writes retain the append-only notification boundary.
internal static class UserLifecycleAuditRepair
{
    internal static async Task<int> RunAsync(NoCtfDbContext db, int expectedCount, CancellationToken ct)
    {
        if (expectedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var candidates = db.Notifications.IgnoreAutoIncludes()
            .Where(item => item.Kind == NotificationKind.UserAccountLifecycleChanged
                && item.UserLifecycleAction == null);
        var rows = await candidates.AsNoTracking()
            .Select(item => new { item.Id, item.ActionValue, item.UserId }).ToArrayAsync(ct);
        if (rows.Length != expectedCount)
            throw new InvalidOperationException("Lifecycle audit repair count differs from the reviewed count.");
        foreach (var row in rows)
        {
            if (row.UserId is null || row.ActionValue is not int value
                || value is < short.MinValue or > short.MaxValue
                || !Enum.IsDefined((UserAccountLifecycleAction)(short)value))
                throw new InvalidOperationException("An imported lifecycle audit has an unreadable action or subject.");
        }
        var updated = 0;
        foreach (var row in rows)
        {
            var action = (UserAccountLifecycleAction)(short)row.ActionValue!.Value;
            updated += await candidates.Where(item => item.Id == row.Id && item.ActionValue == row.ActionValue)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.UserLifecycleAction, (UserAccountLifecycleAction?)action)
                    .SetProperty(item => item.ActionValue, (int?)null), ct);
        }
        if (updated != expectedCount)
            throw new InvalidOperationException("An imported lifecycle audit changed during repair.");
        await transaction.CommitAsync(ct);
        return updated;
    }
}
