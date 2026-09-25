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
    public async Task<NotificationListPage> ListPageAsync(
        Guid userId,
        Guid? competitionId,
        int offset,
        int limit,
        bool desc,
        NotificationReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ApplyScope(await VisibleToAsync(userId, cancellationToken), scope);
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
        return await ProjectAsync(thread, ct);
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
        var sourceNames = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => sourceIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        return notifications.Select(notification => new NotificationView(
                notification.Id,
                notification.SourceType,
                notification.SourceId,
                notification.TargetType,
                notification.TargetId,
                notification.Kind,
                NotificationContentProjection.Create(notification),
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
            return await VisibleToInMemoryAsync(userId, ct);
        var activeAdministrator = db.Users.IgnoreQueryFilters().Any(user =>
            user.Id == userId
            && user.Role == UserRole.Administrator
            && user.AccountStatus == UserAccountStatus.Active);
        var inaccessibleStaffCompetitionIds = db.Competitions.IgnoreQueryFilters()
            .Where(competition =>
                competition.AccessMode == NoCTF.Domain.Competitions.CompetitionAccessMode.StaffOnly
                && !(activeAdministrator
                    || competition.OwnerId == userId
                    || competition.Collaborators.Any(collaborator => collaborator.UserId == userId)))
            .Select(competition => competition.Id);

        var audienceVisible = db.Notifications.AsNoTracking().Where(notification =>
            !(notification.TargetType == NotificationTargetType.User
                && notification.TargetId != userId)
            && !(notification.RelatedType == EntityReferenceKind.Competition
                && notification.RelatedId != null
                && inaccessibleStaffCompetitionIds.Contains(notification.RelatedId.Value))
            && !((notification.TargetType == NotificationTargetType.CompetitionCollaborators
                    || notification.TargetType == NotificationTargetType.CompetitionParticipants)
                && inaccessibleStaffCompetitionIds.Contains(notification.TargetId))
            && !(notification.TargetType == NotificationTargetType.TeamMembers
                && db.Teams.IgnoreQueryFilters().Any(team =>
                    team.Id == notification.TargetId
                    && inaccessibleStaffCompetitionIds.Contains(team.CompetitionId)))
            && !(notification.ThreadRootId != null
                && db.Notifications.Any(root =>
                    root.Id == notification.ThreadRootId
                    && root.RelatedType == EntityReferenceKind.Competition
                    && root.RelatedId != null
                    && inaccessibleStaffCompetitionIds.Contains(root.RelatedId.Value))));

        var directlyVisibleRootIds = audienceVisible
            .Where(notification =>
                notification.TargetType == NotificationTargetType.User
                    && notification.TargetId == userId
                || notification.TargetType == NotificationTargetType.CompetitionCollaborators
                    && db.Competitions.Any(competition =>
                        competition.Id == notification.TargetId
                        && competition.DeletedAt == null
                        && (competition.OwnerId == userId
                            || competition.Collaborators.Any(collaborator =>
                                collaborator.UserId == userId)))
                || notification.TargetType == NotificationTargetType.CompetitionParticipants
                    && db.Teams.Any(team =>
                        team.CompetitionId == notification.TargetId
                        && team.DeletedAt == null
                        && team.RegistrationStatus == TeamRegistrationStatus.Approved
                        && !team.IsBanned
                        && team.Members.Any(member => member.UserId == userId))
                || notification.TargetType == NotificationTargetType.TeamMembers
                    && db.Teams.Any(team =>
                        team.Id == notification.TargetId
                        && team.DeletedAt == null
                        && team.Members.Any(member => member.UserId == userId))
                || notification.TargetType == NotificationTargetType.PlatformAdministrators
                    && activeAdministrator)
            .Select(notification => notification.ThreadRootId ?? notification.Id);
        var participatedRootIds = audienceVisible
            .Where(notification => notification.SourceType == NotificationSourceType.User
                && notification.SourceId == userId)
            .Select(notification => notification.ThreadRootId ?? notification.Id);
        var visibleRootIds = directlyVisibleRootIds.Union(participatedRootIds);

        return audienceVisible
            .Where(notification => visibleRootIds.Contains(
                notification.ThreadRootId ?? notification.Id))
            .Where(item =>
                item.Kind != NotificationKind.AuthenticationSecurityActivity
                && item.Kind != NotificationKind.PlatformUserAccessTokenIssued
                && item.Kind != NotificationKind.PlatformUserAccessTokenRevoked
                && item.Kind != NotificationKind.PlatformUserTokensInvalidated
                && item.Kind != NotificationKind.SsoProviderConfigurationChanged
                && item.Kind != NotificationKind.SsoExternalIdentityBindingChanged);
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
