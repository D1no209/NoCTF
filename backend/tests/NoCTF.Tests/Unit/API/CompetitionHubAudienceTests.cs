using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionHubAudienceTests
{
    [Test]
    public async Task Router_targets_both_groups_for_public_and_only_staff_for_hidden()
    {
        var competitionId = Guid.CreateVersion7();
        var access = Substitute.For<ICompetitionHubAudienceAccess>();
        access.GetAccessModeAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns(
                CompetitionAccessMode.Public,
                CompetitionAccessMode.StaffOnly);
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var clients = Substitute.For<IHubClients<ICompetitionHubClient>>();
        var client = Substitute.For<ICompetitionHubClient>();
        context.Clients.Returns(clients);
        clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(client);
        clients.Group(Arg.Any<string>()).Returns(client);
        var router = new CompetitionHubAudienceRouter(
            access,
            context);

        _ = await router.CurrentAsync(competitionId, CancellationToken.None);
        _ = await router.CurrentAsync(competitionId, CancellationToken.None);

        string[] expectedGroups =
        [
            CompetitionHubGroups.Public(competitionId),
            CompetitionHubGroups.Staff(competitionId)
        ];
        _ = clients.Received(1).Groups(Arg.Is<IReadOnlyList<string>>(groups =>
            groups != null && groups.SequenceEqual(expectedGroups)));
        _ = clients.Received(1).Group(CompetitionHubGroups.Staff(competitionId));
    }

    [Test]
    public async Task Hiding_competition_removes_registered_public_subscriptions()
    {
        var competitionId = Guid.CreateVersion7();
        var connectionId = "connection-1";
        var registry = new CompetitionHubSubscriptionRegistry();
        registry.Set(new(
            connectionId,
            Guid.CreateVersion7(),
            competitionId,
            CompetitionHubGroups.Public(competitionId),
            IsStaff: false));
        var access = Substitute.For<ICompetitionHubAudienceAccess>();
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var groups = Substitute.For<IGroupManager>();
        context.Groups.Returns(groups);
        var coordinator = new CompetitionHubAudienceCoordinator(
            access,
            context,
            registry);
        var change = new CompetitionEventCommitted(
            competitionId,
            Guid.CreateVersion7(),
            CompetitionEventKind.CompetitionAudienceChanged,
            CompetitionEventLevel.Information,
            DateTimeOffset.UnixEpoch,
            CompetitionAudienceChangeKind.AccessMode,
            CompetitionAccessMode.StaffOnly);

        await coordinator.ApplyAsync(change, CancellationToken.None);

        await groups.Received(1).RemoveFromGroupAsync(
            connectionId,
            CompetitionHubGroups.Public(competitionId),
            CancellationToken.None);
        await Assert.That(registry.TryGet(connectionId, competitionId, out _)).IsFalse();
        await access.Received(1).ResolveAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Hiding_competition_promotes_new_staff_before_removing_public_group()
    {
        var competitionId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var connectionId = "connection-1";
        var registry = new CompetitionHubSubscriptionRegistry();
        registry.Set(new(
            connectionId,
            userId,
            competitionId,
            CompetitionHubGroups.Public(competitionId),
            IsStaff: false));
        var access = Substitute.For<ICompetitionHubAudienceAccess>();
        access.ResolveAsync(userId, competitionId, Arg.Any<CancellationToken>())
            .Returns(new CompetitionHubAccessDecision(IsStaff: true));
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var groups = Substitute.For<IGroupManager>();
        context.Groups.Returns(groups);
        var coordinator = new CompetitionHubAudienceCoordinator(
            access,
            context,
            registry);
        var change = new CompetitionEventCommitted(
            competitionId,
            Guid.CreateVersion7(),
            CompetitionEventKind.CompetitionAudienceChanged,
            CompetitionEventLevel.Information,
            DateTimeOffset.UnixEpoch,
            CompetitionAudienceChangeKind.AccessMode,
            CompetitionAccessMode.StaffOnly);

        await coordinator.ApplyAsync(change, CancellationToken.None);

        await groups.Received(1).RemoveFromGroupAsync(
            connectionId,
            CompetitionHubGroups.Public(competitionId),
            CancellationToken.None);
        await groups.Received(1).AddToGroupAsync(
            connectionId,
            CompetitionHubGroups.Staff(competitionId),
            CancellationToken.None);
        await Assert.That(registry.TryGet(connectionId, competitionId, out var subscription))
            .IsTrue();
        await Assert.That(subscription.IsStaff).IsTrue();
    }
}
