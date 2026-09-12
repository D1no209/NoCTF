using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionHubTests
{
    [Test]
    public async Task Authorized_join_only_adds_the_connection_to_the_group()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();
        harness.Access.ResolveAsync(harness.UserId, competitionId, CancellationToken.None)
            .Returns(new CompetitionHubAccessDecision(IsStaff: false));

        await harness.Hub.JoinCompetition(competitionId);

        await harness.Groups.Received(1).AddToGroupAsync(
            harness.ConnectionId,
            CompetitionHubGroups.Public(competitionId),
            CancellationToken.None);
        await harness.Hub.HeartbeatCompetition(competitionId);
    }

    [Test]
    public async Task Heartbeat_requires_a_join_on_the_same_connection()
    {
        var harness = CreateHarness();
        Func<Task> heartbeat = () => harness.Hub.HeartbeatCompetition(Guid.CreateVersion7());
        await Assert.That(heartbeat).Throws<HubException>();
    }

    [Test]
    public async Task Rejected_join_does_not_add_a_group()
    {
        var harness = CreateHarness();
        harness.Access.ResolveAsync(harness.UserId, Arg.Any<Guid>(), CancellationToken.None)
            .Returns((CompetitionHubAccessDecision?)null);
        Func<Task> join = () => harness.Hub.JoinCompetition(Guid.CreateVersion7());

        await Assert.That(join).Throws<HubException>();
        await harness.Groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Staff_subscription_is_revalidated_and_removed_after_access_is_revoked()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();
        harness.Access.ResolveAsync(harness.UserId, competitionId, CancellationToken.None)
            .Returns(
                new CompetitionHubAccessDecision(IsStaff: true),
                (CompetitionHubAccessDecision?)null);

        await harness.Hub.JoinCompetition(competitionId);
        Func<Task> heartbeat = () => harness.Hub.HeartbeatCompetition(competitionId);

        await Assert.That(heartbeat).Throws<HubException>();
        await harness.Groups.Received(1).RemoveFromGroupAsync(
            harness.ConnectionId,
            CompetitionHubGroups.Staff(competitionId),
            CancellationToken.None);
    }

    private static HubHarness CreateHarness()
    {
        var access = Substitute.For<ICompetitionHubAccess>();
        var groups = Substitute.For<IGroupManager>();
        groups.AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var context = Substitute.For<HubCallerContext>();
        var userId = Guid.CreateVersion7();
        const string connectionId = "connection-1";
        context.ConnectionId.Returns(connectionId);
        context.UserIdentifier.Returns(userId.ToString());
        context.ConnectionAborted.Returns(CancellationToken.None);
        context.Items.Returns(new Dictionary<object, object?>());
        var subscriptions = new CompetitionHubSubscriptionRegistry();
        var hub = new CompetitionHub(access, subscriptions)
            { Context = context, Groups = groups };
        return new(hub, access, groups, userId, connectionId);
    }

    private sealed record HubHarness(
        CompetitionHub Hub,
        ICompetitionHubAccess Access,
        IGroupManager Groups,
        Guid UserId,
        string ConnectionId);
}
