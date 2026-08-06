using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

internal sealed record ResourceManagerEligibilityCheck(
    Guid[] MissingUserIds,
    Guid[] RoleIneligibleUserIds)
{
    public bool IsEligible =>
        MissingUserIds.Length == 0 && RoleIneligibleUserIds.Length == 0;
}

internal static class ResourceManagerRoleGuard
{
    public static async Task<ResourceManagerEligibilityCheck> AcquireAndCheckAsync(
        NoCtfDbContext db,
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var normalized = userIds.Distinct().Order().ToArray();
        await AcquireAsync(db, normalized, cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(user => normalized.Contains(user.Id))
            .Select(user => new { user.Id, user.Role })
            .ToListAsync(cancellationToken);
        var existingIds = users.Select(user => user.Id).ToArray();
        return new(
            normalized.Except(existingIds).ToArray(),
            users.Where(user => !user.Role.CanManageResources())
                .Select(user => user.Id)
                .Order()
                .ToArray());
    }

    public static async Task AcquireAsync(
        NoCtfDbContext db,
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return;

        foreach (var userId in userIds.Distinct().Order())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({'u' + userId.ToString("N")}, 0))",
                cancellationToken);
        }
    }
}
