using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.API.SignalR.Publishing;

/// <summary>Development single-process equivalent of the NATS fanout path.</summary>
public sealed class LocalNotificationChangePublisher(
    NotificationChangeAudienceResolver audienceResolver,
    IHubContext<NotificationHub, INotificationHubClient> hub)
    : INotificationChangePublisher
{
    public async Task PublishAsync(
        NotificationChanged change,
        CancellationToken cancellationToken)
    {
        var users = await audienceResolver.ResolveAsync(change.Audiences, cancellationToken);
        if (users.Count == 0) return;
        await hub.Clients.Users(users.Select(id => id.ToString()).ToArray())
            .NotificationChanged(cancellationToken);
    }
}
