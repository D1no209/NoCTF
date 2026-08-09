using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;

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
        var query = await VisibleToAsync(userId, ct);
        return await ListPageAsync(query, beforeCreatedAt, beforeId, limit, ct);
    }

    public async Task<IReadOnlyList<NotificationView>> ListCompetitionAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var query = (await VisibleToAsync(userId, ct)).Where(notification =>
            notification.RelatedType == EntityReferenceKind.Competition
            && notification.RelatedId == competitionId);
        return await ListPageAsync(query, beforeCreatedAt, beforeId, limit, ct);
    }

    private async Task<IReadOnlyList<NotificationView>> ListPageAsync(
        IQueryable<Notification> query,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        if (beforeCreatedAt is { } createdAt && beforeId is { } id)
            query = query.Where(notification =>
                notification.SentAt < createdAt
                || notification.SentAt == createdAt && notification.Id.CompareTo(id) < 0);
        return await Project(query
            .OrderByDescending(notification => notification.SentAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit))
            .ToListAsync(ct);
    }

    public async Task<KeysetNotificationPosition> GetFeedCheckpointAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var checkpoint = await (await VisibleToAsync(userId, ct))
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
        await Project((await VisibleToAsync(userId, ct))
            .Where(notification =>
                notification.SentAt > position.CreatedAt
                    || notification.SentAt == position.CreatedAt
                    && notification.Id.CompareTo(position.Id) > 0)
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id)
            .Take(limit))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<NotificationView>?> ReadThreadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken ct)
    {
        var authorizedRoot = await (await VisibleToAsync(userId, ct))
            .AnyAsync(notification => notification.Id == notificationId, ct);
        if (!authorizedRoot)
            return null;
        IQueryable<Notification> thread;
        if (!db.Database.IsRelational())
        {
            var notifications = await db.Notifications.AsNoTracking().ToListAsync(ct);
            var byId = notifications.ToDictionary(notification => notification.Id);
            var rootId = notificationId;
            var ancestors = new HashSet<Guid> { rootId };
            while (byId.TryGetValue(rootId, out var current)
                && current.ReplyToId is { } parentId
                && ancestors.Add(parentId))
            {
                rootId = parentId;
            }
            var threadIds = new HashSet<Guid> { rootId };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var notification in notifications)
                {
                    if (notification.ReplyToId is not { } parentId
                        || !threadIds.Contains(parentId)
                        || !threadIds.Add(notification.Id))
                        continue;
                    changed = true;
                }
            }
            thread = db.Notifications.AsNoTracking()
                .Where(notification => threadIds.Contains(notification.Id));
        }
        else
        {
            thread = db.Notifications.FromSqlInterpolated($$"""
            WITH RECURSIVE ancestors AS (
                SELECT n.* FROM notifications AS n WHERE n.id = {{notificationId}}
                UNION ALL
                SELECT parent.* FROM notifications AS parent
                JOIN ancestors AS child ON child.reply_to_id = parent.id
            ), root AS (
                SELECT * FROM ancestors
                ORDER BY (reply_to_id IS NULL) DESC, sent_at, id
                LIMIT 1
            ), thread AS (
                SELECT * FROM root
                UNION ALL
                SELECT child.* FROM notifications AS child
                JOIN thread AS parent ON child.reply_to_id = parent.id
            )
            SELECT DISTINCT * FROM thread
            """).AsNoTracking();
        }
        thread = thread
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id);
        return await Project(thread).ToListAsync(ct);
    }

    private IQueryable<NotificationView> Project(IQueryable<Notification> query) =>
        query.Select(notification => new NotificationView(
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
                notification.SentAt,
                notification.SourceId == null
                    ? null
                    : db.Users.IgnoreQueryFilters()
                        .Where(user => user.Id == notification.SourceId)
                        .Select(user => user.UserName)
                        .FirstOrDefault()));

    private async Task<IQueryable<Notification>> VisibleToAsync(
        Guid userId,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await VisibleToInMemoryAsync(userId, ct);
        return db.Notifications.FromSqlInterpolated($$"""
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

    private async Task<IQueryable<Notification>> VisibleToInMemoryAsync(
        Guid userId,
        CancellationToken ct)
    {
        var notifications = await db.Notifications.AsNoTracking().ToListAsync(ct);
        var competitions = (await db.Competitions.AsNoTracking().ToListAsync(ct))
            .ToDictionary(competition => competition.Id);
        var teams = await db.Teams.AsNoTracking().ToListAsync(ct);
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, ct);

        bool IsDirectlyVisible(Notification notification) => notification.TargetType switch
        {
            NotificationTargetType.User => notification.TargetId == userId,
            NotificationTargetType.CompetitionCollaborators =>
                competitions.TryGetValue(notification.TargetId, out var competition)
                && competition.DeletedAt is null
                && (competition.OwnerId == userId
                    || competition.ManagerIds.Contains(userId)
                    || competition.JudgeIds.Contains(userId)
                    || competition.ObserverIds.Contains(userId)),
            NotificationTargetType.CompetitionParticipants => teams.Any(team =>
                team.CompetitionId == notification.TargetId
                && team.DeletedAt is null
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.MemberIds.Contains(userId)),
            NotificationTargetType.TeamMembers => teams.Any(team =>
                team.Id == notification.TargetId
                && team.DeletedAt is null
                && team.MemberIds.Contains(userId)),
            NotificationTargetType.PlatformAdministrators => user is
            {
                Kind: UserKind.Human,
                Role: UserRole.Administrator,
                AccountStatus: UserAccountStatus.Active
            },
            _ => false
        };

        var visibleIds = notifications
            .Where(notification => IsDirectlyVisible(notification))
            .Select(notification => notification.Id)
            .ToHashSet();
        var participatedIds = notifications
            .Where(notification => notification.SourceType == NotificationSourceType.User
                && notification.SourceId == userId)
            .Select(notification => notification.Id)
            .ToHashSet();

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var notification in notifications)
            {
                if (!participatedIds.Contains(notification.Id)
                    || notification.ReplyToId is not { } parentId
                    || !participatedIds.Add(parentId))
                    continue;
                changed = true;
            }
        }
        visibleIds.UnionWith(participatedIds);

        changed = true;
        while (changed)
        {
            changed = false;
            foreach (var notification in notifications)
            {
                if (notification.ReplyToId is not { } parentId
                    || !visibleIds.Contains(parentId)
                    || !visibleIds.Add(notification.Id))
                    continue;
                changed = true;
            }
        }

        return db.Notifications.AsNoTracking()
            .Where(notification => visibleIds.Contains(notification.Id));
    }
}
