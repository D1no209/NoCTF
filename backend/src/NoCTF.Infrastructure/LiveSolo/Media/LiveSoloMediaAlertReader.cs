using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloMediaAlertReader(NoCtfDbContext db) : ILiveSoloMediaAlertReader
{
    public async Task<NotificationAudience?> AudienceAsync(Guid notificationId, CancellationToken ct)
    {
        var notification = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notificationId
            && x.Kind == NotificationKind.LiveSoloMediaInterrupted && x.TargetType == NotificationTargetType.CompetitionCollaborators, ct);
        return notification is null ? null : new(notification.TargetType, notification.TargetId);
    }
}
