using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.API.LiveSolo.Realtime;

public interface ILiveSoloHubClient
{
    Task MatchChanged(LiveSoloMatchChanged change, CancellationToken ct);
}

[Authorize]
public sealed class LiveSoloHub(LiveSoloConnectionGuard guard) : Hub<ILiveSoloHubClient>
{
    public async Task JoinMatch(Guid competitionId, Guid matchId, LiveSoloRealtimeAudience audience)
    {
        if (!Enum.IsDefined(audience) || competitionId == Guid.Empty || matchId == Guid.Empty
            || !await guard.JoinAsync(Context, competitionId, matchId, audience, Context.ConnectionAborted))
            throw new HubException("The Match subscription is not authorized.");
    }
    public async Task HeartbeatMatch(Guid matchId)
    {
        if (!await guard.HeartbeatAsync(Context.ConnectionId, matchId, Context.ConnectionAborted))
            throw new HubException("The Match subscription is no longer authorized.");
    }
    public Task LeaveMatch(Guid matchId) { guard.Leave(Context.ConnectionId, matchId); return Task.CompletedTask; }
    public override Task OnDisconnectedAsync(Exception? exception)
    { guard.Remove(Context.ConnectionId); return base.OnDisconnectedAsync(exception); }
}
