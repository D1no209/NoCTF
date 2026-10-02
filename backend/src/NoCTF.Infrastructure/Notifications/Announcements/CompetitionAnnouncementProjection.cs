using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications.Announcements;

/// <summary>Projects append-only announcement edits and withdrawals without rewriting sent messages.</summary>
internal static class CompetitionAnnouncementProjection
{
    public static IQueryable<Notification> Roots(NoCtfDbContext db, Guid competitionId) => db.Notifications.AsNoTracking()
        .Where(item => item.Kind == NotificationKind.CompetitionAnnouncement && item.SourceType == NotificationSourceType.User && item.ThreadRootId == null
            && item.TargetId == competitionId && item.RelatedType == EntityReferenceKind.Competition && item.RelatedId == competitionId
            && (item.TargetType == NotificationTargetType.CompetitionParticipants || item.TargetType == NotificationTargetType.CompetitionCollaborators));

    private static IQueryable<Notification> Changes(NoCtfDbContext db) => db.Notifications.AsNoTracking()
        .Where(item => item.Kind == NotificationKind.Message && item.RelatedType == EntityReferenceKind.Notification
            && item.ThreadRootId != null && item.RelatedId == item.ThreadRootId
            && (item.ActionValue == (int)CompetitionAnnouncementChangeAction.Edit || item.ActionValue == (int)CompetitionAnnouncementChangeAction.Withdraw)
            && db.Notifications.Any(root => root.Id == item.ThreadRootId && root.Kind == NotificationKind.CompetitionAnnouncement));

    public static bool IsChange(Notification item) => item.Kind == NotificationKind.Message
        && item.RelatedType == EntityReferenceKind.Notification && item.ThreadRootId != null && item.RelatedId == item.ThreadRootId
        && item.ActionValue is (int)CompetitionAnnouncementChangeAction.Edit or (int)CompetitionAnnouncementChangeAction.Withdraw;

    public static IQueryable<Notification> ActiveMessages(NoCtfDbContext db, IQueryable<Notification> query)
    {
        var changes = Changes(db);
        return query.Where(item => !changes.Any(change => change.Id == item.Id))
            .Where(item => item.Kind != NotificationKind.CompetitionAnnouncement
                || !changes.Any(change => change.ThreadRootId == item.Id && change.ActionValue == (int)CompetitionAnnouncementChangeAction.Withdraw));
    }

    public static Task<Dictionary<Guid, Notification>> LatestAsync(NoCtfDbContext db, IReadOnlyList<Notification> roots, CancellationToken ct) =>
        LatestForIdsAsync(db, roots.Where(item => item.Kind == NotificationKind.CompetitionAnnouncement).Select(item => item.Id).ToArray(), ct);

    private static async Task<Dictionary<Guid, Notification>> LatestForIdsAsync(NoCtfDbContext db, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var rows = await Changes(db).Where(item => ids.Contains(item.ThreadRootId!.Value))
            .OrderByDescending(item => item.SentAt).ThenByDescending(item => item.Id).ToArrayAsync(ct);
        return rows.GroupBy(item => item.ThreadRootId!.Value).ToDictionary(group => group.Key, group => group.First());
    }

    public static bool Withdrawn(Notification? change) => change?.ActionValue == (int)CompetitionAnnouncementChangeAction.Withdraw;
    public static string Title(Notification root, Notification? change) => change?.Title ?? root.Title ?? string.Empty;
    public static string Body(Notification root, Notification? change) => change?.Body ?? root.Body ?? string.Empty;

    public static NotificationContent Content(Notification root, Notification? change) => root.Kind == NotificationKind.CompetitionAnnouncement && change is not null
        ? new CompetitionAnnouncementNotificationContent(Title(root, change), Body(root, change), root.TeamId, root.TeamName,
            root.ActionValue is { } action ? (TeamBanAnnouncementKind)action : null)
        : NotificationContentProjection.Create(root);
}
