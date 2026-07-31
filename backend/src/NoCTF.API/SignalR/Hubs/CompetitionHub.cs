using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.API.SignalR.Hubs;

[Authorize]
public sealed class CompetitionHub(
    ICompetitionHubAccess access,
    ILeaderboardSubscriptionRegistry subscriptions,
    IBackendMessagePublisher messages) : Hub
{
    public async Task JoinCompetition(Guid competitionId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId)
            || !await access.CanJoinAsync(userId, competitionId, Context.ConnectionAborted))
            throw new HubException("You are not allowed to join this competition.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"competition:{competitionId:N}", Context.ConnectionAborted);
        JoinedCompetitions().Add(competitionId);
        await TouchSubscriptionAsync(competitionId, Context.ConnectionAborted);
    }

    public Task HeartbeatCompetition(Guid competitionId)
    {
        if (!JoinedCompetitions().Contains(competitionId))
            throw new HubException("Join the competition before renewing its subscription.");
        return TouchSubscriptionAsync(competitionId, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removals = JoinedCompetitions().Select(competitionId =>
            subscriptions.RemoveAsync(competitionId, Context.ConnectionId, CancellationToken.None));
        try
        {
            await Task.WhenAll(removals);
        }
        finally
        {
            await base.OnDisconnectedAsync(exception);
        }
    }

    private async Task TouchSubscriptionAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        if (await subscriptions.TouchAsync(
                competitionId,
                Context.ConnectionId,
                cancellationToken))
            await messages.ProjectLeaderboardAsync(competitionId, cancellationToken);
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
