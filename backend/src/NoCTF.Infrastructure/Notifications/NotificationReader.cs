using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;

namespace NoCTF.Infrastructure.Notifications;

public sealed class NotificationReader(NoCtfDbContext db) : INotificationReader
{
    public async Task<IReadOnlyList<NotificationView>> ListAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking()
            .Where(notification => notification.UserId == userId);
        if (beforeCreatedAt is { } createdAt && beforeId is { } id)
            query = query.Where(notification =>
                notification.CreatedAt < createdAt
                || notification.CreatedAt == createdAt && notification.Id.CompareTo(id) < 0);
        return await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .Select(notification => new NotificationView(
                notification.Id,
                notification.CompetitionId,
                notification.EntityId,
                notification.Kind,
                notification.PayloadJson,
                notification.CreatedAt))
            .ToListAsync(ct);
    }
}
