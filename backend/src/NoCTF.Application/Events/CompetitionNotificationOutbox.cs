using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NoCTF.Application.Events;

public static class CompetitionNotificationTypes
{
    public const string CompetitionStarted = "competition.started";
    public const string ChallengePublished = "challenge.published";
    public const string HintPublished = "hint.published";
    public const string FirstBlood = "blood.first";
    public const string SecondBlood = "blood.second";
    public const string ThirdBlood = "blood.third";
    public const string TeamPenalized = "team.penalized";
    public const string Announcement = "announcement";
}

public sealed record CompetitionNotification(
    Guid CompetitionId,
    string Type,
    string SubjectType,
    Guid? SubjectId,
    Guid? ActorUserId,
    string IdempotencyKey,
    string PayloadJson,
    string Source = "automatic",
    IReadOnlyCollection<Guid>? TargetGroupIds = null,
    Guid? RequestedTemplateId = null,
    Guid? ParentDeliveryId = null,
    Guid? EventId = null)
{
    public static CompetitionNotification Create<TPayload>(
        Guid competitionId,
        string type,
        string subjectType,
        Guid? subjectId,
        Guid? actorUserId,
        string idempotencyKey,
        TPayload payload,
        string source = "automatic",
        IReadOnlyCollection<Guid>? targetGroupIds = null,
        Guid? requestedTemplateId = null,
        Guid? parentDeliveryId = null)
        => new(
            competitionId,
            type,
            subjectType,
            subjectId,
            actorUserId,
            idempotencyKey,
            JsonSerializer.Serialize(payload, JsonOptions),
            source,
            targetGroupIds,
            requestedTemplateId,
            parentDeliveryId,
            null);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public interface ICompetitionNotificationOutbox
{
    void Add(CompetitionNotification notification);
}

public interface ICompetitionNotificationSink
{
    void Add(CompetitionNotification notification);
}

public sealed class CompositeCompetitionNotificationOutbox(
    IEnumerable<ICompetitionNotificationSink> sinks,
    ILogger<CompositeCompetitionNotificationOutbox> logger)
    : ICompetitionNotificationOutbox
{
    public void Add(CompetitionNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        foreach (var sink in sinks)
        {
            try
            {
                sink.Add(notification);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Competition notification sink {SinkType} rejected event {EventType} ({IdempotencyKey})",
                    sink.GetType().Name,
                    notification.Type,
                    notification.IdempotencyKey);
            }
        }
    }
}

public sealed class NullCompetitionNotificationOutbox : ICompetitionNotificationOutbox
{
    public void Add(CompetitionNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
    }
}
