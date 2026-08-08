using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;

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
        var query = VisibleTo(userId);
        if (beforeCreatedAt is { } createdAt && beforeId is { } id)
            query = query.Where(notification =>
                notification.SentAt < createdAt
                || notification.SentAt == createdAt && notification.Id.CompareTo(id) < 0);
        return await query
            .OrderByDescending(notification => notification.SentAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .Select(notification => new NotificationView(
                notification.Id,
                notification.SourceType,
                notification.SourceId,
                notification.TargetType,
                notification.TargetId,
                notification.Kind,
                notification.ContentJson,
                notification.RelatedType,
                notification.RelatedId,
                notification.ReplyToId,
                notification.SentAt))
            .ToListAsync(ct);
    }

    public async Task<KeysetNotificationPosition> GetFeedCheckpointAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var checkpoint = await VisibleTo(userId)
            .OrderByDescending(notification => notification.SentAt)
            .ThenByDescending(notification => notification.Id)
            .Select(notification => new KeysetNotificationPosition(
                notification.SentAt,
                notification.Id))
            .FirstOrDefaultAsync(ct);
        return checkpoint ?? new(now, Guid.Empty);
    }

    public async Task<IReadOnlyList<NotificationView>> ReadFeedAsync(
        Guid userId,
        KeysetNotificationPosition position,
        int limit,
        CancellationToken ct) =>
        await VisibleTo(userId)
            .Where(notification =>
                notification.SentAt > position.CreatedAt
                    || notification.SentAt == position.CreatedAt
                    && notification.Id.CompareTo(position.Id) > 0)
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id)
            .Take(limit)
            .Select(notification => new NotificationView(
                notification.Id,
                notification.SourceType,
                notification.SourceId,
                notification.TargetType,
                notification.TargetId,
                notification.Kind,
                notification.ContentJson,
                notification.RelatedType,
                notification.RelatedId,
                notification.ReplyToId,
                notification.SentAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<NotificationView>?> ReadThreadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken ct)
    {
        var authorizedRoot = await VisibleTo(userId)
            .AnyAsync(notification => notification.Id == notificationId, ct);
        if (!authorizedRoot)
            return null;
        return await db.Notifications.FromSqlInterpolated($$"""
            WITH RECURSIVE thread AS (
                SELECT n.* FROM notifications AS n WHERE n.id = {{notificationId}}
                UNION ALL
                SELECT child.* FROM notifications AS child
                JOIN thread AS parent ON child.reply_to_id = parent.id
            )
            SELECT DISTINCT * FROM thread
            """).AsNoTracking()
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id)
            .Select(notification => new NotificationView(
                notification.Id,
                notification.SourceType,
                notification.SourceId,
                notification.TargetType,
                notification.TargetId,
                notification.Kind,
                notification.ContentJson,
                notification.RelatedType,
                notification.RelatedId,
                notification.ReplyToId,
                notification.SentAt))
            .ToListAsync(ct);
    }

    private IQueryable<Notification> VisibleTo(Guid userId) =>
        db.Notifications.FromSqlInterpolated($$"""
            WITH RECURSIVE directly_visible AS (
                SELECT n.*
                FROM notifications AS n
                WHERE (n.target_type = 0 AND n.target_id = {{userId}})
                   OR (n.target_type = 1 AND EXISTS (
                       SELECT 1 FROM competitions AS c
                       WHERE c.id = n.target_id AND c.deleted_at IS NULL
                         AND (c.owner_id = {{userId}}
                           OR {{userId}} = ANY(c.manager_ids)
                           OR {{userId}} = ANY(c.judge_ids)
                           OR {{userId}} = ANY(c.observer_ids))))
                   OR (n.target_type = 2 AND EXISTS (
                       SELECT 1 FROM teams AS t
                       WHERE t.competition_id = n.target_id AND t.deleted_at IS NULL
                         AND t.registration_status = 1 AND NOT t.is_banned
                         AND {{userId}} = ANY(t.member_ids)))
                   OR (n.target_type = 3 AND EXISTS (
                       SELECT 1 FROM teams AS t
                       WHERE t.id = n.target_id AND t.deleted_at IS NULL
                         AND {{userId}} = ANY(t.member_ids)))
                   OR (n.target_type = 4 AND EXISTS (
                       SELECT 1 FROM users AS u
                       WHERE u.id = {{userId}}
                         AND u.kind = {{(short)UserKind.Human}}
                         AND u.role = {{(short)UserRole.Administrator}}
                         AND u.account_status = {{(short)UserAccountStatus.Active}}))
            ), participated_path AS (
                SELECT n.* FROM notifications AS n
                WHERE n.source_type = {{(short)NotificationSourceType.User}}
                  AND n.source_id = {{userId}}
                UNION
                SELECT parent.* FROM notifications AS parent
                JOIN participated_path AS child ON child.reply_to_id = parent.id
            ), visible AS (
                SELECT * FROM directly_visible
                UNION
                SELECT * FROM participated_path
            ), thread AS (
                SELECT * FROM visible
                UNION
                SELECT child.* FROM notifications AS child
                JOIN thread AS parent ON child.reply_to_id = parent.id
            )
            SELECT DISTINCT * FROM thread
            """).AsNoTracking();
}
