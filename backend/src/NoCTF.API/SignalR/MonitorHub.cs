using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NoCTF.API.SignalR;

/// <summary>
/// Hub for organizer/admin monitoring (submissions, containers, system alerts).
/// Requires Organizer or Admin role. Clients connect with ?competitionId=xxx.
/// </summary>
[Authorize(Roles = "Admin,Organizer")]
public class MonitorHub : Hub<IMonitorClient>
{
    private const string GroupPrefix = "Competition_";

    public override async Task OnConnectedAsync()
    {
        var competitionId = Context.GetHttpContext()?.Request.Query["competitionId"].ToString();

        if (string.IsNullOrWhiteSpace(competitionId) || !Guid.TryParse(competitionId, out _))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"{GroupPrefix}{competitionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var competitionId = Context.GetHttpContext()?.Request.Query["competitionId"].ToString();

        if (!string.IsNullOrWhiteSpace(competitionId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{GroupPrefix}{competitionId}");

        await base.OnDisconnectedAsync(exception);
    }
}
