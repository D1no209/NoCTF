using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

internal static class PlatformUserTokenCriticalSection
{
    public static Task<Notification?> AcquireIssuanceAsync(
        NoCtfDbContext db,
        Guid jwtId,
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? db.Notifications.FromSqlInterpolated(
                    $"SELECT * FROM notifications WHERE id = {jwtId} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken)
            : db.Notifications.AsNoTracking()
                .SingleOrDefaultAsync(notification =>
                    notification.Id == jwtId,
                    cancellationToken);
}
