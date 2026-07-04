using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Permissions;
using System.Security.Claims;

namespace NoCTF.API.SignalR;

/// <summary>
/// Hub for organizer/admin monitoring (submissions, containers, system alerts).
/// Requires Organizer or Admin role. Clients connect with ?competitionId=xxx.
/// </summary>
[Authorize(Roles = "Admin,Organizer")]
public class MonitorHub(ICompetitionPermissionService permissions) : Hub<IMonitorClient>
{
    private const string GroupPrefix = "Competition_";
    public const string AdminLogGroup = "AdminMonitor";

    public override async Task OnConnectedAsync()
    {
        var competitionId = Context.GetHttpContext()?.Request.Query["competitionId"].ToString();

        if (string.IsNullOrWhiteSpace(competitionId) || !Guid.TryParse(competitionId, out var parsedCompetitionId))
        {
            Context.Abort();
            return;
        }

        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId) ||
            !await permissions.CanManageCompetitionAsync(userId, parsedCompetitionId, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"{GroupPrefix}{competitionId}");
        if (Context.User?.IsInRole("Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminLogGroup);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var competitionId = Context.GetHttpContext()?.Request.Query["competitionId"].ToString();

        if (!string.IsNullOrWhiteSpace(competitionId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{GroupPrefix}{competitionId}");
        if (Context.User?.IsInRole("Admin") == true)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, AdminLogGroup);

        await base.OnDisconnectedAsync(exception);
    }
}
