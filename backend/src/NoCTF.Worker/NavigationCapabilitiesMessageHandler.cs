using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;

namespace NoCTF.Worker;

public sealed class NavigationCapabilitiesMessageHandler(INotificationChangePublisher publisher)
{
    public Task Handle(NavigationCapabilitiesChanged message, CancellationToken ct) =>
        publisher.PublishAsync(new(message.UserIds.Distinct()
            .Select(userId => new NotificationAudience(NotificationTargetType.User, userId)).ToArray(),
            NotificationChangeKind.NavigationCapabilities), ct);
}
