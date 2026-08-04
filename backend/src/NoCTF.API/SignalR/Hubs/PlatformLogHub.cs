using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NoCTF.API.SignalR.Hubs;

[Authorize(Roles = "Administrator")]
public sealed class PlatformLogHub : Hub
{
    internal const string AdministratorsGroup = "platform-log-administrators";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            AdministratorsGroup,
            Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
