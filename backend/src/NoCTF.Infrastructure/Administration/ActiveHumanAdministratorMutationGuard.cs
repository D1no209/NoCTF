using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

internal static class ActiveHumanAdministratorMutationGuard
{
    public static async Task AcquireAsync(
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsInMemory())
            return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "The active administrator mutation lock requires an explicit transaction.");

        // A table lock is acquired before the first data read. This matters for Serializable
        // deletion transactions: a row/advisory lock could wait with an already-stale snapshot.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"LOCK TABLE users IN SHARE ROW EXCLUSIVE MODE",
            cancellationToken);
    }

    public static bool Contains(User user) =>
        user.Kind == UserKind.Human
        && user.Role == UserRole.Administrator
        && user.AccountStatus == UserAccountStatus.Active;

    public static Task<int> CountAsync(
        NoCtfDbContext db,
        CancellationToken cancellationToken) =>
        Query(db).CountAsync(cancellationToken);

    private static IQueryable<User> Query(NoCtfDbContext db) =>
        db.Users.Where(user =>
            user.Kind == UserKind.Human
            && user.Role == UserRole.Administrator
            && user.AccountStatus == UserAccountStatus.Active);
}
