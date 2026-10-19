using NoCTF.Domain.Notifications;

namespace NoCTF.Application.Notifications;

public readonly record struct NotificationAudience(
    NotificationTargetType TargetType,
    Guid TargetId,
    Guid? ThreadRootId = null);

public enum NotificationChangeKind { Content, NavigationCapabilities }
public sealed record NotificationChanged(NotificationAudience[] Audiences, NotificationChangeKind Kind = NotificationChangeKind.Content);

public interface INotificationChangePublisher
{
    Task PublishAsync(NotificationChanged change, CancellationToken cancellationToken);
}
