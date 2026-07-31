using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionHubTests
{
    [Test]
    public async Task First_authorized_join_tracks_subscription_and_projects_leaderboard()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();
        harness.Access.CanJoinAsync(
                harness.UserId,
                competitionId,
                CancellationToken.None)
            .Returns(true);
        harness.Subscriptions.TouchAsync(
                competitionId,
                harness.ConnectionId,
                CancellationToken.None)
            .Returns(true);

        await harness.Hub.JoinCompetition(competitionId);

        await harness.Groups.Received(1).AddToGroupAsync(
            harness.ConnectionId,
            $"competition:{competitionId:N}",
            CancellationToken.None);
        await harness.Subscriptions.Received(1).TouchAsync(
            competitionId,
            harness.ConnectionId,
            CancellationToken.None);
        await harness.Messages.Received(1).ProjectLeaderboardAsync(
            competitionId,
            CancellationToken.None);
    }

    [Test]
    public async Task Heartbeat_only_renews_a_competition_joined_by_the_connection()
    {
        var harness = CreateHarness();
        var joinedCompetitionId = Guid.CreateVersion7();
        var otherCompetitionId = Guid.CreateVersion7();
        harness.Access.CanJoinAsync(
                harness.UserId,
                joinedCompetitionId,
                CancellationToken.None)
            .Returns(true);
        harness.Subscriptions.TouchAsync(
                joinedCompetitionId,
                harness.ConnectionId,
                CancellationToken.None)
            .Returns(false, true);
        await harness.Hub.JoinCompetition(joinedCompetitionId);
        harness.Subscriptions.ClearReceivedCalls();
        harness.Messages.ClearReceivedCalls();

        await harness.Hub.HeartbeatCompetition(joinedCompetitionId);
        Func<Task> rejectedHeartbeat = () =>
            harness.Hub.HeartbeatCompetition(otherCompetitionId);

        await harness.Subscriptions.Received(1).TouchAsync(
            joinedCompetitionId,
            harness.ConnectionId,
            CancellationToken.None);
        await harness.Messages.Received(1).ProjectLeaderboardAsync(
            joinedCompetitionId,
            CancellationToken.None);
        await Assert.That(rejectedHeartbeat).Throws<HubException>();
        await harness.Subscriptions.DidNotReceive().TouchAsync(
            otherCompetitionId,
            harness.ConnectionId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Disconnect_removes_every_subscription_owned_by_the_connection()
    {
        var harness = CreateHarness();
        var firstCompetitionId = Guid.CreateVersion7();
        var secondCompetitionId = Guid.CreateVersion7();
        harness.Access.CanJoinAsync(
                harness.UserId,
                Arg.Any<Guid>(),
                CancellationToken.None)
            .Returns(true);
        harness.Subscriptions.TouchAsync(
                Arg.Any<Guid>(),
                harness.ConnectionId,
                CancellationToken.None)
            .Returns(false);
        await harness.Hub.JoinCompetition(firstCompetitionId);
        await harness.Hub.JoinCompetition(secondCompetitionId);
        harness.Subscriptions.ClearReceivedCalls();

        await harness.Hub.OnDisconnectedAsync(null);

        await harness.Subscriptions.Received(1).RemoveAsync(
            firstCompetitionId,
            harness.ConnectionId,
            CancellationToken.None);
        await harness.Subscriptions.Received(1).RemoveAsync(
            secondCompetitionId,
            harness.ConnectionId,
            CancellationToken.None);
    }

    [Test]
    public async Task Rejected_join_does_not_create_a_subscription()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();
        harness.Access.CanJoinAsync(
                harness.UserId,
                competitionId,
                CancellationToken.None)
            .Returns(false);
        Func<Task> join = () => harness.Hub.JoinCompetition(competitionId);

        await Assert.That(join).Throws<HubException>();
        await harness.Groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await harness.Subscriptions.DidNotReceive().TouchAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await harness.Messages.DidNotReceive().ProjectLeaderboardAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    private static HubHarness CreateHarness()
    {
        var access = Substitute.For<ICompetitionHubAccess>();
        var subscriptions = Substitute.For<ILeaderboardSubscriptionRegistry>();
        var messages = Substitute.For<IBackendMessagePublisher>();
        var groups = Substitute.For<IGroupManager>();
        groups.AddToGroupAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var context = Substitute.For<HubCallerContext>();
        var userId = Guid.CreateVersion7();
        const string connectionId = "connection-1";
        context.ConnectionId.Returns(connectionId);
        context.UserIdentifier.Returns(userId.ToString());
        context.ConnectionAborted.Returns(CancellationToken.None);
        context.Items.Returns(new Dictionary<object, object?>());
        var hub = new CompetitionHub(access, subscriptions, messages)
        {
            Context = context,
            Groups = groups
        };
        return new(hub, access, subscriptions, messages, groups, userId, connectionId);
    }

    private sealed record HubHarness(
        CompetitionHub Hub,
        ICompetitionHubAccess Access,
        ILeaderboardSubscriptionRegistry Subscriptions,
        IBackendMessagePublisher Messages,
        IGroupManager Groups,
        Guid UserId,
        string ConnectionId);
}
