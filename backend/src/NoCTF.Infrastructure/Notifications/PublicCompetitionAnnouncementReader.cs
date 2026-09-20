using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

public sealed class PublicCompetitionAnnouncementReader(NoCtfDbContext db)
    : IPublicCompetitionAnnouncementReader
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

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
            .Select(notification => new
            {
                notification.Id,
                notification.ContentJson,
                notification.SentAt
            })
            .ToArrayAsync(ct);
        var items = rows.Select(row =>
        {
            var content = JsonSerializer.Deserialize<AnnouncementContent>(
                row.ContentJson,
                JsonOptions) ?? throw new InvalidOperationException(
                "Competition announcement content is invalid.");
            if (content.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(content.Title)
                || string.IsNullOrWhiteSpace(content.Body))
            {
                throw new InvalidOperationException(
                    "Competition announcement content uses an unsupported schema.");
            }
            return new PublicCompetitionAnnouncementView(
                row.Id,
                content.Title,
                content.Body,
                row.SentAt);
        }).ToArray();
        return new(PublicCompetitionAnnouncementReadState.Available, items);
    }

    private sealed record AnnouncementContent(
        int SchemaVersion,
        string Title,
        string Body);
}
