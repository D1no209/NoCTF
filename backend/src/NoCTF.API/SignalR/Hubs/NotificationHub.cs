using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Observability;

namespace NoCTF.API.SignalR.Hubs;

public interface INotificationHubClient
{
    [HubMethodName("notificationChanged")]
    Task NotificationChanged(CancellationToken cancellationToken);
}

[Authorize]
public sealed class NotificationHub : Hub<INotificationHubClient>
{
    public override async Task OnConnectedAsync()
    {
        NoCtfTelemetry.SignalRConnected("notifications");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        NoCtfTelemetry.SignalRDisconnected("notifications");
        await base.OnDisconnectedAsync(exception);
    }
}
