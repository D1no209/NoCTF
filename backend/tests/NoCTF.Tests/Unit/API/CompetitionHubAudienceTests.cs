using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionHubAudienceTests
{
    [Test]
    [Arguments(CompetitionAccessMode.Public, 2)]
    [Arguments(CompetitionAccessMode.StaffOnly, 1)]
    public async Task Router_targets_the_audience_and_revalidates_only_non_public_pushes(CompetitionAccessMode mode, int candidateCount)
    {
        var competitionId = Guid.NewGuid(); var otherCompetitionId = Guid.NewGuid(); var userId = Guid.NewGuid();
        var registry = new CompetitionHubSubscriptionRegistry();
        registry.Set(new("public", userId, competitionId, CompetitionHubGroups.Public(competitionId), false));
        registry.Set(new("staff", userId, competitionId, CompetitionHubGroups.Staff(competitionId), true));
        registry.Set(new("other", userId, otherCompetitionId, CompetitionHubGroups.Public(otherCompetitionId), false));
        var validator = ApprovingValidator();
        var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        foreach (var id in new[] { "public", "staff", "other", "unsubscribed" })
            guard.Register(Caller(id, userId, DateTimeOffset.UtcNow.AddHours(1)), MfaHubKind.Competition);
        var access = Substitute.For<ICompetitionHubAudienceAccess>();
        access.GetAccessModeAsync(competitionId, Arg.Any<CancellationToken>()).Returns(mode);
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var router = new CompetitionHubAudienceRouter(access, context, registry, guard,
            Substitute.For<IHubContext<SpectatorHub, ISpectatorHubClient>>());

        _ = await router.CurrentAsync(competitionId, CancellationToken.None);

        _ = context.Clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids != null && ids.Count == candidateCount
            && ids.All(value => value == "staff" || mode == CompetitionAccessMode.Public && value == "public")));
        await validator.Received(mode == CompetitionAccessMode.Public ? 0 : 1).ValidateAsync(
            Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>());
    }

    private static IMfaConnectionContextValidator ApprovingValidator()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<string, MfaFailure?>)call.Arg<IReadOnlyList<MfaContextValidationRequest>>()!
                .ToDictionary(value => value.Key, _ => (MfaFailure?)null));
        return validator;
    }

    [Test]
    public async Task An_expired_connection_cannot_receive_a_push()
    {
        var competitionId = Guid.NewGuid(); var userId = Guid.NewGuid();
        var registry = new CompetitionHubSubscriptionRegistry();
        registry.Set(new("staff", userId, competitionId, CompetitionHubGroups.Staff(competitionId), true));
        var caller = Caller("staff", userId, DateTimeOffset.UtcNow.AddMinutes(-1));
        var guard = new MfaConnectionGuard(Substitute.For<IMfaConnectionContextValidator>(), TimeProvider.System);
        guard.Register(caller, MfaHubKind.Competition);
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var router = new CompetitionHubAudienceRouter(Substitute.For<ICompetitionHubAudienceAccess>(), context, registry, guard,
            Substitute.For<IHubContext<SpectatorHub, ISpectatorHubClient>>());

        _ = await router.AllKnownAsync(competitionId, CancellationToken.None);

        _ = context.Clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids != null && ids.Count == 0));
        caller.Received(1).Abort();
        await Assert.That(guard.IsRegistered("staff")).IsFalse();
    }

    [Test]
    [Arguments(CompetitionEventVisibility.Team, new[] { "member", "staff" })]
    [Arguments(CompetitionEventVisibility.Staff, new[] { "staff" })]
    [Arguments(CompetitionEventVisibility.Public, new[] { "member", "other", "staff" })]
    public async Task Events_reach_only_connections_that_may_read_them(CompetitionEventVisibility visibility, string[] expected)
    {
        var competitionId = Guid.NewGuid(); var teamId = Guid.NewGuid();
        var member = Guid.NewGuid(); var other = Guid.NewGuid(); var staff = Guid.NewGuid();
        var registry = new CompetitionHubSubscriptionRegistry();
        registry.Set(new("member", member, competitionId, CompetitionHubGroups.Public(competitionId), false));
        registry.Set(new("other", other, competitionId, CompetitionHubGroups.Public(competitionId), false));
        registry.Set(new("staff", staff, competitionId, CompetitionHubGroups.Staff(competitionId), true));
        var guard = new MfaConnectionGuard(ApprovingValidator(), TimeProvider.System);
        guard.Register(Caller("member", member, DateTimeOffset.UtcNow.AddHours(1)), MfaHubKind.Competition);
        guard.Register(Caller("other", other, DateTimeOffset.UtcNow.AddHours(1)), MfaHubKind.Competition);
        guard.Register(Caller("staff", staff, DateTimeOffset.UtcNow.AddHours(1)), MfaHubKind.Competition);
        var access = Substitute.For<ICompetitionHubAudienceAccess>();
        access.GetAccessModeAsync(competitionId, Arg.Any<CancellationToken>()).Returns(CompetitionAccessMode.Public);
        access.GetTeamMemberIdsAsync(teamId, Arg.Any<CancellationToken>()).Returns(new[] { member });
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var router = new CompetitionHubAudienceRouter(access, context, registry, guard,
            Substitute.For<IHubContext<SpectatorHub, ISpectatorHubClient>>());

        _ = await router.ForEventAsync(competitionId, visibility, teamId, CancellationToken.None);

        _ = context.Clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids != null
            && ids.Order().SequenceEqual(expected)));
    }

    private static HubCallerContext Caller(string connectionId, Guid userId, DateTimeOffset expires)
    {
        var caller = Substitute.For<HubCallerContext>();
        caller.ConnectionId.Returns(connectionId); caller.UserIdentifier.Returns(userId.ToString());
        caller.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new("token_version", "0"),
            new("exp", expires.ToUnixTimeSeconds().ToString())], "Bearer")));
        return caller;
    }

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
        clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(client);
        clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(client);
        clients.Group(Arg.Any<string>()).Returns(client);
        var publicUser = Guid.NewGuid(); var staffUser = Guid.NewGuid();
        var registry = new CompetitionHubSubscriptionRegistry();
        registry.Set(new("public", publicUser, competitionId, CompetitionHubGroups.Public(competitionId), false));
        registry.Set(new("staff", staffUser, competitionId, CompetitionHubGroups.Staff(competitionId), true));
        var router = new CompetitionHubAudienceRouter(access, context, registry,
            MfaHubTestSupport.Guard(("public", publicUser, MfaHubKind.Competition), ("staff", staffUser, MfaHubKind.Competition)),
            Substitute.For<IHubContext<SpectatorHub, ISpectatorHubClient>>());

        _ = await router.CurrentAsync(competitionId, CancellationToken.None);
        _ = await router.CurrentAsync(competitionId, CancellationToken.None);

        _ = clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids != null && ids.Count == 2 && ids.Contains("public") && ids.Contains("staff")));
        _ = clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids != null && ids.Count == 1 && ids.Contains("staff")));
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
