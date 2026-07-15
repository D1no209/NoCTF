using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Infrastructure;

namespace NoCTF.API.SignalR;

/// <summary>
/// Hub for real-time leaderboard updates, scoped to a competition group.
/// Clients connect with ?competitionId=xxx query string.
/// </summary>
[Authorize]
public class LeaderboardHub(ApplicationDbContext db) : Hub<ILeaderboardClient>
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

        if (!await CompetitionRealtimeAccess.CanJoinCompetitionGroupAsync(Context, db, parsedCompetitionId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"{GroupPrefix}{parsedCompetitionId:D}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var competitionId = Context.GetHttpContext()?.Request.Query["competitionId"].ToString();

        if (Guid.TryParse(competitionId, out var parsedCompetitionId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{GroupPrefix}{parsedCompetitionId:D}");

        await base.OnDisconnectedAsync(exception);
    }
}
