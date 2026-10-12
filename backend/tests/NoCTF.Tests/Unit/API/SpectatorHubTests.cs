using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.API;

public sealed class SpectatorHubTests
{
    [Test]
    public async Task Public_hub_passes_the_Mfa_filter_without_registering_an_authenticated_connection()
    {
        var guard = MfaHubTestSupport.Guard(); var filter = new MfaHubFilter(guard);
        var context = Substitute.For<HubCallerContext>(); context.ConnectionId.Returns("anonymous");
        var hub = new SpectatorHub(Substitute.For<ICompetitionVisibilityAccess>(), TimeProvider.System);
        using var services = new ServiceCollection().BuildServiceProvider();
        var connected = false;
        await filter.OnConnectedAsync(new HubLifetimeContext(context, services, hub), _ => { connected = true; return Task.CompletedTask; });
        var invocation = new HubInvocationContext(context, services, hub, typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.JoinSpectator))!, [Guid.NewGuid()]);
        var invoked = false;
        await filter.InvokeMethodAsync(invocation, _ => { invoked = true; return ValueTask.FromResult<object?>(null); });
        await Assert.That(connected && invoked).IsTrue();
        await Assert.That(guard.IsRegistered("anonymous")).IsFalse();
    }

    [Test]
    public async Task Even_an_authenticated_operator_joins_using_anonymous_visibility_and_only_one_group()
    {
        var access = Substitute.For<ICompetitionVisibilityAccess>();
        access.ResolveAsync(Arg.Is<Guid>(id => id == Guid.Empty), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CompetitionVisibilityAccessDecision(GameMode.Awd, CompetitionStatus.Running,
                CompetitionLeaderboardVisibility.Normal, LeaderboardDataScope.Live));
        var context = Substitute.For<HubCallerContext>(); context.ConnectionId.Returns("screen");
        context.UserIdentifier.Returns(Guid.NewGuid().ToString()); context.Items.Returns(new Dictionary<object, object?>());
        var groups = Substitute.For<IGroupManager>();
        var hub = new SpectatorHub(access, TimeProvider.System) { Context = context, Groups = groups };
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await hub.JoinSpectator(first); await hub.JoinSpectator(second);
        await groups.Received().AddToGroupAsync("screen", SpectatorHub.Group(first), Arg.Any<CancellationToken>());
        await groups.Received().RemoveFromGroupAsync("screen", SpectatorHub.Group(first), Arg.Any<CancellationToken>());
        await groups.Received().AddToGroupAsync("screen", SpectatorHub.Group(second), Arg.Any<CancellationToken>());
        await access.DidNotReceive().ResolveAsync(Arg.Is<Guid>(id => id != Guid.Empty), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Staff_only_competitions_cannot_be_subscribed_to()
    {
        var access = Substitute.For<ICompetitionVisibilityAccess>();
        var context = Substitute.For<HubCallerContext>();
        var hub = new SpectatorHub(access, TimeProvider.System) { Context = context, Groups = Substitute.For<IGroupManager>() };
        var join = () => hub.JoinSpectator(Guid.NewGuid());
        await Assert.That(join).Throws<HubException>();
    }

    [Test]
    public async Task Relay_sends_only_competition_identity_to_the_public_client()
    {
        var audience = Substitute.For<ICompetitionHubClient>(); var spectators = Substitute.For<ISpectatorHubClient>();
        var change = new ScoreboardUpdated(Guid.NewGuid(), "private-version", "private-schema", "private-catalog");
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        context.Clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(audience);
        var spectatorContext = Substitute.For<IHubContext<SpectatorHub, ISpectatorHubClient>>();
        spectatorContext.Clients.Group(SpectatorHub.Group(change.CompetitionId)).Returns(spectators);
        var router = new CompetitionHubAudienceRouter(Substitute.For<ICompetitionHubAudienceAccess>(), context,
            new CompetitionHubSubscriptionRegistry(), MfaHubTestSupport.Guard(), spectatorContext);
        var relay = await router.AllKnownAsync(change.CompetitionId, default);
        await relay.ScoreboardUpdated(change, default);
        await spectators.Received(1).Changed(change.CompetitionId, Arg.Any<CancellationToken>());
        await audience.Received(1).ScoreboardUpdated(change, Arg.Any<CancellationToken>());
        await Assert.That(typeof(ISpectatorHubClient).GetMethods().Length).IsEqualTo(1);
    }
}
