using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.API.SignalR.Hubs;

[Authorize]
public sealed class CompetitionHub(
    ICompetitionHubAccess access) : Hub<ICompetitionHubClient>
{
    public override async Task OnConnectedAsync()
    {
        NoCtfTelemetry.SignalRConnected("competition");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        NoCtfTelemetry.SignalRDisconnected("competition");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinCompetition(Guid competitionId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId)
            || !await access.CanJoinAsync(userId, competitionId, Context.ConnectionAborted))
            throw new HubException("You are not allowed to join this competition.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"competition:{competitionId:N}", Context.ConnectionAborted);
        JoinedCompetitions().Add(competitionId);
    }

    public Task HeartbeatCompetition(Guid competitionId)
    {
        if (!JoinedCompetitions().Contains(competitionId))
            throw new HubException("Join the competition before renewing its subscription.");
        return Task.CompletedTask;
    }

    private HashSet<Guid> JoinedCompetitions()
    {
        const string key = "leaderboard-subscriptions";
        if (Context.Items.TryGetValue(key, out var value)
            && value is HashSet<Guid> competitions)
            return competitions;
        competitions = [];
        Context.Items[key] = competitions;
        return competitions;
    }
}
