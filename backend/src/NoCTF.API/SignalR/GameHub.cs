using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.SignalR;

/// <summary>
/// Hub for game event notifications (flag solves, challenge updates, competition state).
/// Clients connect with ?competitionId=xxx query string.
/// </summary>
[Authorize]
public class GameHub(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions) : Hub<IGameNotificationClient>
{
    private const string GroupPrefix = "Competition_";

    public override async Task OnConnectedAsync()
    {
        var competitionId = Context.GetHttpContext()?.Request.Query["competitionId"].ToString();

        if (string.IsNullOrWhiteSpace(competitionId) || !Guid.TryParse(competitionId, out var parsedCompetitionId))
        {
            Context.Abort();
            return;
        }

        if (!await CompetitionRealtimeAccess.CanJoinCompetitionGroupAsync(Context, db, permissions, parsedCompetitionId))
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
