using NoCTF.Domain.Notifications;

namespace NoCTF.Application.Messaging;

public sealed record CreateNotification(
    Guid CompetitionId,
    Guid? TeamId,
    Guid? UserId,
    NotificationKind Kind,
    string Payload,
    long ProcessingVersion);

public sealed record DeliverNotification(
    Guid NotificationId,
    long ProcessingVersion);
