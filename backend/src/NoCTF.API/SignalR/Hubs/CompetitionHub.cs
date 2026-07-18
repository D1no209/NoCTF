using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NoCTF.API.SignalR.Hubs;

[Authorize]
public sealed class CompetitionHub : Hub
{
    public Task JoinCompetition(Guid competitionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"competition:{competitionId:N}");
}
