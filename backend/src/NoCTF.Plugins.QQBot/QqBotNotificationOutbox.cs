using System.Text.Json;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Events;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.QQBot;

internal sealed class QqBotNotificationOutbox(ApplicationDbContext db) : ICompetitionNotificationSink
{
    public void Add(CompetitionNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var eventType = MapEventType(notification.Type);
        var source = MapSource(notification.Source);
        var eventId = notification.EventId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.QqBotEvents.Add(new QqBotEvent
        {
            Id = eventId,
            CompetitionId = notification.CompetitionId,
            EventType = eventType,
            Status = QqBotEventStatus.Pending,
            Source = source,
            SubjectType = Limit(notification.SubjectType, 128),
            SubjectId = notification.SubjectId,
            ActorUserId = notification.ActorUserId,
            IdempotencyKey = Limit(notification.IdempotencyKey, 512),
            PayloadJson = notification.PayloadJson,
            TargetGroupIdsJson = notification.TargetGroupIds is null
                ? null
                : JsonSerializer.Serialize(notification.TargetGroupIds, JsonOptions),
            RequestedTemplateId = notification.RequestedTemplateId,
            ParentDeliveryId = notification.ParentDeliveryId,
            CreatedAt = now
        });

        db.BackgroundTasks.Add(new BackgroundTaskItem
        {
            Id = Guid.NewGuid(),
            CompetitionId = notification.CompetitionId,
            Type = QqBotDispatchJobHandler.JobKeyValue,
            Status = BackgroundTaskStatus.Pending,
            PayloadJson = JsonSerializer.Serialize(new QqBotDispatchPayload(eventId), JsonOptions),
            MaxAttempts = 3,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static QqBotEventType MapEventType(string type) => type switch
    {
        CompetitionNotificationTypes.CompetitionStarted => QqBotEventType.CompetitionStarted,
        CompetitionNotificationTypes.ChallengePublished => QqBotEventType.ChallengePublished,
        CompetitionNotificationTypes.HintPublished => QqBotEventType.HintPublished,
        CompetitionNotificationTypes.FirstBlood => QqBotEventType.FirstBlood,
        CompetitionNotificationTypes.SecondBlood => QqBotEventType.SecondBlood,
        CompetitionNotificationTypes.ThirdBlood => QqBotEventType.ThirdBlood,
        CompetitionNotificationTypes.TeamPenalized => QqBotEventType.TeamPenalized,
        CompetitionNotificationTypes.Announcement => QqBotEventType.Announcement,
        _ => throw new InvalidOperationException($"Unsupported competition notification type '{type}'.")
    };

    private static QqBotDeliverySource MapSource(string source) => source.Trim().ToLowerInvariant() switch
    {
        "automatic" => QqBotDeliverySource.Automatic,
        "manual" => QqBotDeliverySource.Manual,
        "test" => QqBotDeliverySource.Test,
        "manual-retry" => QqBotDeliverySource.ManualRetry,
        _ => throw new InvalidOperationException($"Unsupported QQBot notification source '{source}'.")
    };

    private static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed record QqBotDispatchPayload(Guid EventId);
