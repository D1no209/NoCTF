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
    public Task<IReadOnlyList<NotificationView>> ListAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct) =>
        ListAsync(userId, beforeCreatedAt, beforeId, limit, NotificationReadScope.All, ct);

    public async Task<IReadOnlyList<NotificationView>> ListAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        NotificationReadScope scope,
        CancellationToken ct)
    {
        var query = await VisibleToAsync(userId, ct);
        query = ApplyScope(query, scope);
        return await ListPageAsync(query, beforeCreatedAt, beforeId, limit, ct);
    }

    public Task<IReadOnlyList<NotificationView>> ListCompetitionAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct) =>
        ListCompetitionAsync(
            userId,
            competitionId,
            beforeCreatedAt,
            beforeId,
            limit,
            NotificationReadScope.All,
            ct);

    public async Task<IReadOnlyList<NotificationView>> ListCompetitionAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        NotificationReadScope scope,
        CancellationToken ct)
    {
        var query = ApplyScope(await VisibleToAsync(userId, ct), scope).Where(notification =>
            notification.RelatedType == EntityReferenceKind.Competition
            && notification.RelatedId == competitionId);
        return await ListPageAsync(query, beforeCreatedAt, beforeId, limit, ct);
    }

    private static IQueryable<Notification> ApplyScope(
        IQueryable<Notification> query,
        NotificationReadScope scope) =>
        scope == NotificationReadScope.Inbox
            ? query.Where(notification =>
                notification.TargetType != NotificationTargetType.CompetitionParticipants
                || notification.Kind == NotificationKind.CompetitionAnnouncement
                    && notification.SourceType == NotificationSourceType.User)
            : query;

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
        var selected = await (await VisibleToAsync(userId, ct))
            .Where(notification => notification.Id == notificationId)
            .Select(notification => new { notification.Id, notification.ThreadRootId })
            .SingleOrDefaultAsync(ct);
        if (selected is null)
            return null;
        var rootId = selected.ThreadRootId ?? selected.Id;
        var thread = db.Notifications.AsNoTracking()
            .Where(notification => notification.Id == rootId
                || notification.ThreadRootId == rootId)
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id);
        return await Project(thread).ToListAsync(ct);
    }

    private IQueryable<NotificationView> Project(IQueryable<Notification> query) =>
        query.Where(notification => notification.Kind != NotificationKind.AuthenticationSecurityActivity
                && notification.Kind != NotificationKind.HttpCommandReceipt
                && notification.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && notification.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && notification.Kind != NotificationKind.PlatformUserTokensInvalidated)
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
                notification.ThreadRootId,
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
            WITH notification_scope AS (
                SELECT n.id,
                       COALESCE(
                           CASE
                               WHEN n.related_type = {{(short)EntityReferenceKind.Competition}}
                                   THEN n.related_id
                               WHEN n.target_type IN (
                                   {{(short)NotificationTargetType.CompetitionCollaborators}},
                                   {{(short)NotificationTargetType.CompetitionParticipants}})
                                   THEN n.target_id
                               WHEN n.target_type = {{(short)NotificationTargetType.TeamMembers}}
                                   THEN (
                                       SELECT t.competition_id
                                       FROM teams AS t
                                       WHERE t.id = n.target_id)
                           END,
                           CASE
                               WHEN n.thread_root_id IS NOT NULL THEN (
                                   SELECT root.related_id
                                   FROM notifications AS root
                                   WHERE root.id = n.thread_root_id
                                     AND root.related_type = {{(short)EntityReferenceKind.Competition}})
                           END) AS competition_id
                FROM notifications AS n
            ), audience_visible AS (
                SELECT n.*
                FROM notifications AS n
                JOIN notification_scope AS scope ON scope.id = n.id
                WHERE scope.competition_id IS NULL
                   OR NOT EXISTS (
                       SELECT 1
                       FROM competitions AS c
                       WHERE c.id = scope.competition_id
                         AND c.access_mode = {{(short)NoCTF.Domain.Competitions.CompetitionAccessMode.StaffOnly}}
                         AND NOT EXISTS (
                             SELECT 1
                             FROM users AS u
                             WHERE u.id = {{userId}}
                               AND u.account_status = {{(short)UserAccountStatus.Active}}
                               AND (u.role = {{(short)UserRole.Administrator}}
                                 OR c.owner_id = {{userId}}
                                 OR {{userId}} = ANY(c.manager_ids)
                                 OR {{userId}} = ANY(c.judge_ids)
                                 OR {{userId}} = ANY(c.observer_ids))))
            ), directly_visible AS (
                SELECT n.id,
                       COALESCE(n.thread_root_id, n.id) AS root_id
                FROM audience_visible AS n
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
            ), participated_roots AS (
                SELECT DISTINCT COALESCE(n.thread_root_id, n.id) AS root_id
                FROM audience_visible AS n
                WHERE n.source_type = {{(short)NotificationSourceType.User}}
                  AND n.source_id = {{userId}}
            ), visible_roots AS (
                SELECT root_id FROM directly_visible
                UNION
                SELECT root_id FROM participated_roots
            )
            SELECT n.*
            FROM audience_visible AS n
            JOIN visible_roots AS visible
              ON n.id = visible.root_id OR n.thread_root_id = visible.root_id
            """).AsNoTracking().Where(item =>
                item.Kind != NotificationKind.AuthenticationSecurityActivity
                && item.Kind != NotificationKind.HttpCommandReceipt
                && item.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && item.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && item.Kind != NotificationKind.PlatformUserTokensInvalidated);
    }

    private async Task<IQueryable<Notification>> VisibleToInMemoryAsync(
        Guid userId,
        CancellationToken ct)
    {
        var notifications = await db.Notifications.AsNoTracking().ToListAsync(ct);
        var competitions = (await db.Competitions.IgnoreQueryFilters().AsNoTracking()
                .ToListAsync(ct))
            .ToDictionary(competition => competition.Id);
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var teamsById = teams.ToDictionary(team => team.Id);
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, ct);

        Guid? AudienceCompetitionId(Notification notification)
        {
            if (notification.RelatedType == EntityReferenceKind.Competition)
                return notification.RelatedId;
            if (notification.TargetType is NotificationTargetType.CompetitionCollaborators
                or NotificationTargetType.CompetitionParticipants)
                return notification.TargetId;
            if (notification.TargetType == NotificationTargetType.TeamMembers
                && teamsById.TryGetValue(notification.TargetId, out var team))
                return team.CompetitionId;
            if (notification.ThreadRootId is { } rootId)
            {
                var root = notifications.FirstOrDefault(candidate => candidate.Id == rootId);
                if (root?.RelatedType == EntityReferenceKind.Competition)
                    return root.RelatedId;
            }
            return null;
        }

        bool IsAudienceVisible(Notification notification)
        {
            var competitionId = AudienceCompetitionId(notification);
            if (competitionId is null
                || !competitions.TryGetValue(competitionId.Value, out var competition)
                || competition.AccessMode !=
                    NoCTF.Domain.Competitions.CompetitionAccessMode.StaffOnly)
                return true;
            return user is { AccountStatus: UserAccountStatus.Active }
                && (user.Role == UserRole.Administrator
                    || competition.OwnerId == userId
                    || competition.ManagerIds.Contains(userId)
                    || competition.JudgeIds.Contains(userId)
                    || competition.ObserverIds.Contains(userId));
        }

        var audienceVisible = notifications.Where(IsAudienceVisible).ToArray();
        var audienceVisibleIds = audienceVisible
            .Select(notification => notification.Id)
            .ToHashSet();

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

        var visibleRootIds = audienceVisible
            .Where(notification => IsDirectlyVisible(notification))
            .Select(notification => notification.ThreadRootId ?? notification.Id)
            .ToHashSet();
        visibleRootIds.UnionWith(audienceVisible
            .Where(notification => notification.SourceType == NotificationSourceType.User
                && notification.SourceId == userId)
            .Select(notification => notification.ThreadRootId ?? notification.Id));

        return db.Notifications.AsNoTracking()
            .Where(notification =>
                notification.Kind != NotificationKind.AuthenticationSecurityActivity
                && notification.Kind != NotificationKind.HttpCommandReceipt
                && notification.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && notification.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && notification.Kind != NotificationKind.PlatformUserTokensInvalidated)
            .Where(notification => visibleRootIds.Contains(
                notification.ThreadRootId ?? notification.Id))
            .Where(notification => audienceVisibleIds.Contains(notification.Id));
    }
}
