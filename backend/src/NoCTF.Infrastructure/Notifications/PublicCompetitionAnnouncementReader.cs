using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Notifications.Announcements;

namespace NoCTF.Infrastructure.Notifications;

public sealed class PublicCompetitionAnnouncementReader(NoCtfDbContext db)
    : IPublicCompetitionAnnouncementReader
{
    public async Task<PublicCompetitionAnnouncementPage> ListAsync(
        Guid competitionId,
        DateTimeOffset? beforePublishedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var isPublic = await db.Competitions.AsNoTracking()
            .AnyAsync(competition =>
                competition.Id == competitionId
                && competition.AccessMode == CompetitionAccessMode.Public
                && competition.Status != CompetitionStatus.Draft,
                ct);
        if (!isPublic)
            return new(PublicCompetitionAnnouncementReadState.CompetitionNotFound);

        var query = db.Notifications.AsNoTracking()
            .Where(notification =>
                notification.Kind == NotificationKind.CompetitionAnnouncement
                && notification.TargetType == NotificationTargetType.CompetitionParticipants
                && notification.TargetId == competitionId
                && notification.RelatedType == EntityReferenceKind.Competition
                && notification.RelatedId == competitionId
                && db.CompetitionEvents.Any(competitionEvent =>
                    competitionEvent.CompetitionId == competitionId
                    && competitionEvent.Kind == CompetitionEventKind.AnnouncementPublished
                    && competitionEvent.Visibility == CompetitionEventVisibility.Public
                    && competitionEvent.SubjectType == EntityReferenceKind.Notification
                    && competitionEvent.SubjectId == notification.Id));
        query = CompetitionAnnouncementProjection.ActiveMessages(db, query);
        if (beforePublishedAt is { } publishedAt && beforeId is { } id)
        {
            query = query.Where(notification =>
                notification.SentAt < publishedAt
                || notification.SentAt == publishedAt
                && notification.Id.CompareTo(id) < 0);
        }

        var rows = await query
            .OrderByDescending(notification => notification.SentAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .ToArrayAsync(ct);
        var changes = await CompetitionAnnouncementProjection.LatestAsync(db, rows, ct);
        var items = rows.Select(row =>
        {
            var change = changes.GetValueOrDefault(row.Id);
            var title = CompetitionAnnouncementProjection.Title(row, change);
            var body = CompetitionAnnouncementProjection.Body(row, change);
            if (string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException(
                    "Competition announcement content is incomplete.");
            }
            return new PublicCompetitionAnnouncementView(
                row.Id,
                title,
                body,
                row.SentAt);
        }).ToArray();
        return new(PublicCompetitionAnnouncementReadState.Available, items);
    }
}
