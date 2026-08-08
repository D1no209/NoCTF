using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Notifications;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

public sealed class CompetitionNotificationDelivery(
    NoCtfDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

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
            SentAt = DateTimeOffset.UtcNow
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
                && user.Kind == NoCTF.Domain.Identity.UserKind.Human
                && user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        if (recipients.Length == 0)
            return;

        _ = entityId;
        var sentAt = DateTimeOffset.UtcNow;
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

    public async Task<NotificationView?> CreateAnnouncementAsync(
        Guid competitionId,
        Guid sourceUserId,
        string title,
        string body,
        bool participants,
        DateTimeOffset sentAt,
        CancellationToken ct)
    {
        var exists = await db.Competitions.AsNoTracking()
            .AnyAsync(competition => competition.Id == competitionId, ct);
        if (!exists)
            return null;
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(sentAt),
            SourceType = NotificationSourceType.User,
            SourceId = sourceUserId,
            TargetType = participants
                ? NotificationTargetType.CompetitionParticipants
                : NotificationTargetType.CompetitionCollaborators,
            TargetId = competitionId,
            Kind = NotificationKind.CompetitionAnnouncement,
            ContentJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                subject = title,
                title,
                body
            }, JsonOptions),
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = competitionId,
            SentAt = sentAt
        };
        db.Notifications.Add(notification);
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
