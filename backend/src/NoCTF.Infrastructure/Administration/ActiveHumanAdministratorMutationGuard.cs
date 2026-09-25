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
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "The active administrator guard requires an explicit serializable transaction.");
        await Task.CompletedTask;
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
