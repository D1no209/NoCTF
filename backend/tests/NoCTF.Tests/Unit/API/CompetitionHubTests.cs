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
        harness.Access.CanJoinAsync(harness.UserId, competitionId, CancellationToken.None)
            .Returns(true);

        await harness.Hub.JoinCompetition(competitionId);

        await harness.Groups.Received(1).AddToGroupAsync(
            harness.ConnectionId,
            $"competition:{competitionId:N}",
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
        harness.Access.CanJoinAsync(harness.UserId, Arg.Any<Guid>(), CancellationToken.None)
            .Returns(false);
        Func<Task> join = () => harness.Hub.JoinCompetition(Guid.CreateVersion7());

        await Assert.That(join).Throws<HubException>();
        await harness.Groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        var hub = new CompetitionHub(access) { Context = context, Groups = groups };
        return new(hub, access, groups, userId, connectionId);
    }

    private sealed record HubHarness(
        CompetitionHub Hub,
        ICompetitionHubAccess Access,
        IGroupManager Groups,
        Guid UserId,
        string ConnectionId);
}
