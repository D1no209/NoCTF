using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Notifications.Announcements;

namespace NoCTF.Infrastructure.Notifications;

public sealed class NotificationReader(NoCtfDbContext db) : INotificationReader
{
    public async Task<NotificationListPage> ListPageAsync(
        Guid userId,
        Guid? competitionId,
        int offset,
        int limit,
        bool desc,
        NotificationReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ApplyScope(await VisibleToAsync(userId, cancellationToken), scope, userId);
        if (competitionId is { } id)
            query = query.Where(notification =>
                notification.RelatedType == EntityReferenceKind.Competition
                && notification.RelatedId == id);

        var total = await query.CountAsync(cancellationToken);
        var ordered = desc
            ? query.OrderByDescending(notification => notification.SentAt)
                .ThenByDescending(notification => notification.Id)
            : query.OrderBy(notification => notification.SentAt)
                .ThenBy(notification => notification.Id);
        var items = await ProjectAsync(
            ordered.Skip(offset).Take(limit),
            cancellationToken);
        return new NotificationListPage(items, total);
    }

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
        query = ApplyScope(query, scope, userId);
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
        var query = ApplyScope(await VisibleToAsync(userId, ct), scope, userId).Where(notification =>
            notification.RelatedType == EntityReferenceKind.Competition
            && notification.RelatedId == competitionId);
        return await ListPageAsync(query, beforeCreatedAt, beforeId, limit, ct);
    }

    private static IQueryable<Notification> ApplyScope(
        IQueryable<Notification> query,
        NotificationReadScope scope,
        Guid userId) =>
        scope == NotificationReadScope.Inbox
            ? query.Where(notification =>
                (notification.TargetType != NotificationTargetType.CompetitionParticipants
                    || notification.Kind == NotificationKind.CompetitionAnnouncement
                        && notification.SourceType == NotificationSourceType.User)
                && !(notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == userId))
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
        return await ProjectAsync(query
            .OrderByDescending(notification => notification.SentAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit), ct);
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
        await ProjectAsync((await VisibleToAsync(userId, ct))
            .Where(notification =>
                notification.SentAt > position.CreatedAt
                    || notification.SentAt == position.CreatedAt
                    && notification.Id.CompareTo(position.Id) > 0)
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id)
            .Take(limit), ct);

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
                || notification.ThreadRootId == rootId
                    && notification.TargetType != NotificationTargetType.User)
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id);
        return await ProjectAsync(CompetitionAnnouncementProjection.ActiveMessages(db, thread), ct);
    }

    private async Task<IReadOnlyList<NotificationView>> ProjectAsync(
        IQueryable<Notification> query,
        CancellationToken ct)
    {
        var notifications = await query.Where(notification =>
                notification.Kind != NotificationKind.AuthenticationSecurityActivity
                && notification.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && notification.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && notification.Kind != NotificationKind.PlatformUserTokensInvalidated
                && notification.Kind != NotificationKind.SsoProviderConfigurationChanged
                && notification.Kind != NotificationKind.SsoExternalIdentityBindingChanged)
            .ToArrayAsync(ct);
        var sourceIds = notifications.Where(notification => notification.SourceId != null)
            .Select(notification => notification.SourceId!.Value)
            .Distinct()
            .ToArray();
        var sourceNames = sourceIds.Length == 0 ? new Dictionary<Guid, string>() : await db.Users.IgnoreQueryFilters()
            .Where(user => sourceIds.Contains(user.Id))
            .Select(user => new { user.Id, user.UserName })
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        var changes = await CompetitionAnnouncementProjection.LatestAsync(db, notifications, ct);
        return notifications.Select(notification => new NotificationView(
                notification.Id,
                notification.SourceType,
                notification.SourceId,
                notification.TargetType,
                notification.TargetId,
                notification.Kind,
                CompetitionAnnouncementProjection.Content(notification, changes.GetValueOrDefault(notification.Id)),
                notification.RelatedType,
                notification.RelatedId,
                notification.ThreadRootId,
                notification.ReplyToId,
                notification.SentAt,
                notification.SourceId is { } sourceId
                    ? sourceNames.GetValueOrDefault(sourceId)
                    : null)).ToArray();
    }

    private async Task<IQueryable<Notification>> VisibleToAsync(
        Guid userId,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return CompetitionAnnouncementProjection.ActiveMessages(db, await VisibleToInMemoryAsync(userId, ct));

        // Resolve the caller's audience once so the notification predicates become sargable
        // (target_type, target_id) / (source_type, source_id) / thread_root_id lookups instead of
        // correlated membership subqueries repeated for every row.
        var audience = await LoadAudienceAsync(userId, ct);
        var audienceVisible = AudienceVisible(userId, audience);

        // A thread is visible when any of its audience-visible messages is addressed to or sent by the
        // caller. Only threaded messages and roots that actually have replies can widen visibility, so
        // the root set is materialized from those rows; standalone messages are matched directly below.
        var threadRootIds = await audienceVisible
            .Where(notification =>
                notification.TargetType == NotificationTargetType.User
                    && notification.TargetId == userId
                || notification.TargetType == NotificationTargetType.CompetitionCollaborators
                    && audience.CollaboratorCompetitionIds.Contains(notification.TargetId)
                || notification.TargetType == NotificationTargetType.CompetitionParticipants
                    && audience.ParticipantCompetitionIds.Contains(notification.TargetId)
                || notification.TargetType == NotificationTargetType.TeamMembers
                    && audience.MemberTeamIds.Contains(notification.TargetId)
                || notification.TargetType == NotificationTargetType.PlatformAdministrators
                    && audience.ActiveAdministrator
                || notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == userId)
            .Where(notification => notification.ThreadRootId != null
                || db.Notifications.Any(reply => reply.ThreadRootId == notification.Id))
            .Select(notification => notification.ThreadRootId ?? notification.Id)
            .Distinct()
            .ToArrayAsync(ct);

        var visible = audienceVisible
            .Where(notification =>
                notification.TargetType == NotificationTargetType.User
                    && notification.TargetId == userId
                || notification.TargetType == NotificationTargetType.CompetitionCollaborators
                    && audience.CollaboratorCompetitionIds.Contains(notification.TargetId)
                || notification.TargetType == NotificationTargetType.CompetitionParticipants
                    && audience.ParticipantCompetitionIds.Contains(notification.TargetId)
                || notification.TargetType == NotificationTargetType.TeamMembers
                    && audience.MemberTeamIds.Contains(notification.TargetId)
                || notification.TargetType == NotificationTargetType.PlatformAdministrators
                    && audience.ActiveAdministrator
                || notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == userId
                || notification.ThreadRootId == null
                    && threadRootIds.Contains(notification.Id)
                || notification.ThreadRootId != null
                    && threadRootIds.Contains(notification.ThreadRootId.Value))
            .Where(item =>
                item.Kind != NotificationKind.AuthenticationSecurityActivity
                && item.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && item.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && item.Kind != NotificationKind.PlatformUserTokensInvalidated
                && item.Kind != NotificationKind.SsoProviderConfigurationChanged
                && item.Kind != NotificationKind.SsoExternalIdentityBindingChanged);
        return visible;
    }

    private IQueryable<Notification> AudienceVisible(Guid userId, NotificationAudience audience)
    {
        var query = db.Notifications.AsNoTracking().Where(notification =>
            !(notification.TargetType == NotificationTargetType.User
                && notification.TargetId != userId));
        if (audience.InaccessibleCompetitionIds.Length > 0)
        {
            var inaccessible = audience.InaccessibleCompetitionIds;
            query = query.Where(notification =>
                !(notification.RelatedType == EntityReferenceKind.Competition
                    && notification.RelatedId != null
                    && inaccessible.Contains(notification.RelatedId.Value))
                && !((notification.TargetType == NotificationTargetType.CompetitionCollaborators
                        || notification.TargetType == NotificationTargetType.CompetitionParticipants)
                    && inaccessible.Contains(notification.TargetId))
                && !(notification.TargetType == NotificationTargetType.TeamMembers
                    && db.Teams.IgnoreQueryFilters().Any(team =>
                        team.Id == notification.TargetId
                        && inaccessible.Contains(team.CompetitionId)))
                && !(notification.ThreadRootId != null
                    && db.Notifications.Any(root =>
                        root.Id == notification.ThreadRootId
                        && root.RelatedType == EntityReferenceKind.Competition
                        && root.RelatedId != null
                        && inaccessible.Contains(root.RelatedId.Value))));
        }
        return CompetitionAnnouncementProjection.ActiveMessages(db, query);
    }

    /// <summary>Loads every audience key of the caller in one round trip.</summary>
    private async Task<NotificationAudience> LoadAudienceAsync(Guid userId, CancellationToken ct)
    {
        var activeAdministrator = db.Users.IgnoreQueryFilters().Where(user =>
            user.Id == userId
            && user.Role == UserRole.Administrator
            && user.AccountStatus == UserAccountStatus.Active);
        var keys = await activeAdministrator
            .Select(user => new AudienceKey { Kind = AudienceKeyKind.ActiveAdministrator, Id = user.Id })
            .Concat(db.Competitions.IgnoreQueryFilters()
                .Where(competition =>
                    competition.AccessMode == NoCTF.Domain.Competitions.CompetitionAccessMode.StaffOnly
                    && competition.OwnerId != userId
                    && !competition.Collaborators.Any(collaborator => collaborator.UserId == userId)
                    && !activeAdministrator.Any())
                .Select(competition => new AudienceKey
                    { Kind = AudienceKeyKind.InaccessibleCompetition, Id = competition.Id }))
            .Concat(db.Competitions
                .Where(competition =>
                    competition.DeletedAt == null
                    && (competition.OwnerId == userId
                        || competition.Collaborators.Any(collaborator => collaborator.UserId == userId)))
                .Select(competition => new AudienceKey
                    { Kind = AudienceKeyKind.CollaboratorCompetition, Id = competition.Id }))
            .Concat(db.Teams
                .Where(team =>
                    team.DeletedAt == null
                    && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !team.IsBanned
                    && team.Members.Any(member => member.UserId == userId))
                .Select(team => new AudienceKey
                    { Kind = AudienceKeyKind.ParticipantCompetition, Id = team.CompetitionId }))
            .Concat(db.Teams
                .Where(team =>
                    team.DeletedAt == null
                    && team.Members.Any(member => member.UserId == userId))
                .Select(team => new AudienceKey { Kind = AudienceKeyKind.MemberTeam, Id = team.Id }))
            .ToArrayAsync(ct);
        Guid[] Ids(AudienceKeyKind kind) => keys.Where(key => key.Kind == kind)
            .Select(key => key.Id)
            .Distinct()
            .ToArray();
        return new NotificationAudience(
            keys.Any(key => key.Kind == AudienceKeyKind.ActiveAdministrator),
            Ids(AudienceKeyKind.InaccessibleCompetition),
            Ids(AudienceKeyKind.CollaboratorCompetition),
            Ids(AudienceKeyKind.ParticipantCompetition),
            Ids(AudienceKeyKind.MemberTeam));
    }

    private enum AudienceKeyKind : short
    {
        ActiveAdministrator,
        InaccessibleCompetition,
        CollaboratorCompetition,
        ParticipantCompetition,
        MemberTeam
    }

    private sealed class AudienceKey
    {
        public AudienceKeyKind Kind { get; init; }
        public Guid Id { get; init; }
    }

    private sealed record NotificationAudience(
        bool ActiveAdministrator,
        Guid[] InaccessibleCompetitionIds,
        Guid[] CollaboratorCompetitionIds,
        Guid[] ParticipantCompetitionIds,
        Guid[] MemberTeamIds);

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
            if (notification.TargetType == NotificationTargetType.User
                && notification.TargetId != userId)
                return false;
            var competitionId = AudienceCompetitionId(notification);
            if (competitionId is null
                || !competitions.TryGetValue(competitionId.Value, out var competition)
                || competition.AccessMode !=
                    NoCTF.Domain.Competitions.CompetitionAccessMode.StaffOnly)
                return true;
            return user is { AccountStatus: UserAccountStatus.Active }
                && (user.Role == UserRole.Administrator
                    || competition.OwnerId == userId
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer && collaborator.UserId == userId));
        }

        var audienceVisible = notifications.Where(IsAudienceVisible)
            .Where(item => !CompetitionAnnouncementProjection.IsChange(item)
                || !notifications.Any(root => root.Id == item.ThreadRootId && root.Kind == NotificationKind.CompetitionAnnouncement)).ToArray();
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
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer && collaborator.UserId == userId)),
            NotificationTargetType.CompetitionParticipants => teams.Any(team =>
                team.CompetitionId == notification.TargetId
                && team.DeletedAt is null
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.Members.Any(member => member.UserId == userId)),
            NotificationTargetType.TeamMembers => teams.Any(team =>
                team.Id == notification.TargetId
                && team.DeletedAt is null
                && team.Members.Any(member => member.UserId == userId)),
            NotificationTargetType.PlatformAdministrators => user is
            {
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
                && notification.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && notification.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && notification.Kind != NotificationKind.PlatformUserTokensInvalidated
                && notification.Kind != NotificationKind.SsoProviderConfigurationChanged
                && notification.Kind != NotificationKind.SsoExternalIdentityBindingChanged)
            .Where(notification => visibleRootIds.Contains(
                notification.ThreadRootId ?? notification.Id))
            .Where(notification => audienceVisibleIds.Contains(notification.Id));
    }
}
