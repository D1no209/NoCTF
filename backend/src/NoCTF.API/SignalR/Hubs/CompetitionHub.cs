using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.API.SignalR.Hubs;

[Authorize]
public sealed class CompetitionHub(
    ICompetitionHubAccess access,
    CompetitionHubSubscriptionRegistry subscriptions) : Hub<ICompetitionHubClient>
{
    public override async Task OnConnectedAsync()
    {
        NoCtfTelemetry.SignalRConnected("competition");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.RemoveConnection(Context.ConnectionId);
        NoCtfTelemetry.SignalRDisconnected("competition");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinCompetition(Guid competitionId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId))
            throw new HubException("You are not allowed to join this competition.");
        var decision = await access.ResolveAsync(
            userId,
            competitionId,
            Context.ConnectionAborted);
        if (decision is null)
            throw new HubException("You are not allowed to join this competition.");

        var groupName = decision.IsStaff
            ? CompetitionHubGroups.Staff(competitionId)
            : CompetitionHubGroups.Public(competitionId);
        if (subscriptions.TryGet(Context.ConnectionId, competitionId, out var previous))
        {
            if (previous.GroupName == groupName)
                return;
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                previous.GroupName,
                Context.ConnectionAborted);
        }
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            groupName,
            Context.ConnectionAborted);
        subscriptions.Set(new(
            Context.ConnectionId,
            userId,
            competitionId,
            groupName,
            decision.IsStaff));
    }

    public async Task HeartbeatCompetition(Guid competitionId)
    {
        if (!subscriptions.TryGet(
                Context.ConnectionId,
                competitionId,
                out var subscription))
            throw new HubException("Join the competition before renewing its subscription.");
        if (!subscription.IsStaff)
            return;

        var decision = await access.ResolveAsync(
            subscription.UserId,
            competitionId,
            Context.ConnectionAborted);
        if (decision is null)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                subscription.GroupName,
                Context.ConnectionAborted);
            subscriptions.Remove(Context.ConnectionId, competitionId);
            throw new HubException("You are not allowed to join this competition.");
        }
        if (decision.IsStaff)
            return;

        var publicGroup = CompetitionHubGroups.Public(competitionId);
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            subscription.GroupName,
            Context.ConnectionAborted);
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            publicGroup,
            Context.ConnectionAborted);
        subscriptions.Set(subscription with
        {
            GroupName = publicGroup,
            IsStaff = false
        });
    }
}
