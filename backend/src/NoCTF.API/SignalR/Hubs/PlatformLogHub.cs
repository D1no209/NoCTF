using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Observability;

namespace NoCTF.API.SignalR.Hubs;

[Authorize(Roles = "Administrator")]
public sealed class PlatformLogHub : Hub<IPlatformLogHubClient>
{
    internal const string AdministratorsGroup = "platform-log-administrators";

    public override async Task OnConnectedAsync()
    {
        NoCtfTelemetry.SignalRConnected("platform_logs");
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            AdministratorsGroup,
            Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        NoCtfTelemetry.SignalRDisconnected("platform_logs");
        await base.OnDisconnectedAsync(exception);
    }
}
