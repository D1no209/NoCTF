using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Notifications;
using NoCTF.Application.Notifications;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

public sealed class CompetitionNotificationDelivery(
    NoCtfDbContext db,
    ICompetitionEventRecorder? eventRecorder = null,
    TimeProvider? clock = null)
    : ICompetitionAnnouncementPublisher
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task DeliverAsync<TPayload>(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        TPayload payload,
        Guid? requiredTeamId,
        CancellationToken ct)
    {
        _ = entityId;
        var targetType = requiredTeamId is null
            ? NotificationTargetType.CompetitionParticipants
            : NotificationTargetType.TeamMembers;
        var targetId = requiredTeamId ?? competitionId;
        var contentJson = EnsureObjectPayload(payload, sourceEventKey);
        var alreadyDelivered = await db.Notifications.AsNoTracking().AnyAsync(
            item => item.TargetType == targetType
                && item.TargetId == targetId
                && item.Kind == kind
                && item.ContentJson == contentJson
                && item.RelatedType == EntityReferenceKind.Competition
                && item.RelatedId == competitionId,
            ct);
        if (alreadyDelivered)
            return;
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            SourceType = NotificationSourceType.System,
            TargetType = targetType,
            TargetId = targetId,
            Kind = kind,
            ContentJson = contentJson,
            RelatedType = NoCTF.Domain.Shared.EntityReferenceKind.Competition,
            RelatedId = competitionId,
            SentAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeliverToUsersAsync<TPayload>(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        TPayload payload,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct)
    {
        if (recipientUserIds.Count == 0)
            return;
        var recipients = await db.Users.AsNoTracking()
            .Where(user => recipientUserIds.Contains(user.Id)
                && user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        if (recipients.Length == 0)
            return;

        _ = entityId;
        var sentAt = timeProvider.GetUtcNow();
        var contentJson = EnsureObjectPayload(payload, sourceEventKey);
        var alreadyDelivered = await db.Notifications.AsNoTracking()
            .Where(item => item.TargetType == NotificationTargetType.User
                && recipients.Contains(item.TargetId)
                && item.Kind == kind
                && item.ContentJson == contentJson
                && item.RelatedType == EntityReferenceKind.Competition
                && item.RelatedId == competitionId)
            .Select(item => item.TargetId)
            .ToArrayAsync(ct);
        var pendingRecipients = recipients.Except(alreadyDelivered).ToArray();
        db.Notifications.AddRange(pendingRecipients.Select((userId, index) => new Notification
        {
            Id = Guid.CreateVersion7(sentAt.AddTicks(index)),
            SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.User,
            TargetId = userId,
            Kind = kind,
            ContentJson = contentJson,
            RelatedType = NoCTF.Domain.Shared.EntityReferenceKind.Competition,
            RelatedId = competitionId,
            SentAt = sentAt
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationView?> PublishAsync(
        PublishCompetitionAnnouncementCommand command,
        CancellationToken ct)
    {
        var exists = await db.Competitions.AsNoTracking()
            .AnyAsync(competition => competition.Id == command.CompetitionId, ct);
        if (!exists)
            return null;
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(command.PublishedAt),
            SourceType = NotificationSourceType.User,
            SourceId = command.ActorUserId,
            TargetType = command.Audience == CompetitionAnnouncementAudience.Participants
                ? NotificationTargetType.CompetitionParticipants
                : NotificationTargetType.CompetitionCollaborators,
            TargetId = command.CompetitionId,
            Kind = NotificationKind.CompetitionAnnouncement,
            ContentJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                subject = command.Title,
                title = command.Title,
                body = command.Body
            }, JsonOptions),
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = command.CompetitionId,
            SentAt = command.PublishedAt
        };
        db.Notifications.Add(notification);
        await events.RecordAsync(new(
            command.CompetitionId,
            CompetitionEventKind.AnnouncementPublished,
            CompetitionEventLevel.Information,
            command.Audience == CompetitionAnnouncementAudience.Participants
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            command.PublishedAt,
            ActorUserId: command.ActorUserId,
            QuestionId: notification.Id), ct);
        await db.SaveChangesAsync(ct);
        return new(
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
            notification.SentAt);
    }

    private static string EnsureObjectPayload<TPayload>(
        TPayload payload,
        string sourceEventKey)
    {
        var element = JsonSerializer.SerializeToElement(payload, JsonOptions);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return JsonSerializer.Serialize(
                new { schemaVersion = 1, sourceEventKey, value = element },
                JsonOptions);
        }
        var values = element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);
        values.TryAdd("schemaVersion", JsonSerializer.SerializeToElement(1));
        values["sourceEventKey"] = JsonSerializer.SerializeToElement(sourceEventKey);
        return JsonSerializer.Serialize(values, JsonOptions);
    }
}
