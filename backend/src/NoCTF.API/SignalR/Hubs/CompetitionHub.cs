using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Notifications;

namespace NoCTF.API.SignalR.Hubs;

[Authorize]
public sealed class CompetitionHub(ICompetitionHubAccess access) : Hub
{
    public async Task JoinCompetition(Guid competitionId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId)
            || !await access.CanJoinAsync(userId, competitionId, Context.ConnectionAborted))
            throw new HubException("You are not allowed to join this competition.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"competition:{competitionId:N}", Context.ConnectionAborted);
    }
}
