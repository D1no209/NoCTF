using NoCTF.Domain.Notifications;

namespace NoCTF.Application.Notifications;

public readonly record struct NotificationAudience(
    NotificationTargetType TargetType,
    Guid TargetId,
    Guid? ThreadRootId = null);

public sealed record NotificationChanged(NotificationAudience[] Audiences);

public interface INotificationChangePublisher
{
    Task PublishAsync(NotificationChanged change, CancellationToken cancellationToken);
}
