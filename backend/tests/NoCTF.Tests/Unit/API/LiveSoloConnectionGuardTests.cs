using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NoCTF.API.LiveSolo.Realtime;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.Tests.Unit.API;

public sealed class LiveSoloConnectionGuardTests
{
    [Test]
    public async Task Private_push_rechecks_the_exact_match_and_disconnects_revoked_audiences()
    {
        var user = Guid.NewGuid(); var context = Context(user); var access = Access();
        var guard = new LiveSoloConnectionGuard(access, MfaHubTestSupport.Guard(("live", user, MfaHubKind.LiveSolo)));
        var competition = Guid.NewGuid(); var match = Guid.NewGuid();
        await Assert.That(await guard.JoinAsync(context, competition, match, LiveSoloRealtimeAudience.Staff, CancellationToken.None)).IsTrue();
        await Assert.That(await guard.EligibleAsync(competition, Guid.NewGuid(), CancellationToken.None)).IsEmpty();
        await Assert.That(await guard.EligibleAsync(competition, match, CancellationToken.None)).Contains("live");
        access.EligibleAsync(Arg.Any<IReadOnlyList<LiveSoloRealtimeAccessRequest>>(), CancellationToken.None).Returns(new HashSet<string>());
        await Assert.That(await guard.EligibleAsync(competition, match, CancellationToken.None)).IsEmpty();
        context.Received(1).Abort();
        await Assert.That(await guard.HeartbeatAsync("live", match, CancellationToken.None)).IsFalse();
    }
    [Test]
    public async Task Missing_mfa_qualification_or_database_failure_never_delivers_a_private_event()
    {
        var user = Guid.NewGuid(); var context = Context(user); var access = Access();
        var guard = new LiveSoloConnectionGuard(access, MfaHubTestSupport.Guard());
        await Assert.That(await guard.JoinAsync(context, Guid.NewGuid(), Guid.NewGuid(), LiveSoloRealtimeAudience.Participant, CancellationToken.None)).IsFalse();
        context.Received(1).Abort();
        context.ClearReceivedCalls();
        guard = new(access, MfaHubTestSupport.Guard(("live", user, MfaHubKind.LiveSolo)));
        var competition = Guid.NewGuid(); var match = Guid.NewGuid();
        await guard.JoinAsync(context, competition, match, LiveSoloRealtimeAudience.Participant, CancellationToken.None);
        access.EligibleAsync(Arg.Any<IReadOnlyList<LiveSoloRealtimeAccessRequest>>(), CancellationToken.None)
            .Returns(Task.FromException<IReadOnlySet<string>>(new IOException("Unavailable")));
        await Assert.That(await guard.EligibleAsync(competition, match, CancellationToken.None)).IsEmpty();
        context.Received(1).Abort();
    }
    [Test]
    public async Task Leave_removes_only_the_selected_match_and_a_connection_cannot_subscribe_without_bound()
    {
        var user = Guid.NewGuid(); var context = Context(user); var access = Access();
        var guard = new LiveSoloConnectionGuard(access, MfaHubTestSupport.Guard(("live", user, MfaHubKind.LiveSolo)));
        var competition = Guid.NewGuid(); var matches = Enumerable.Range(0, 16).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var match in matches) await Assert.That(await guard.JoinAsync(context, competition, match, LiveSoloRealtimeAudience.Participant, CancellationToken.None)).IsTrue();
        await Assert.That(await guard.JoinAsync(context, competition, Guid.NewGuid(), LiveSoloRealtimeAudience.Participant, CancellationToken.None)).IsFalse();
        guard.Leave("live", matches[0]);
        await Assert.That(await guard.HeartbeatAsync("live", matches[0], CancellationToken.None)).IsFalse();
        await Assert.That(await guard.HeartbeatAsync("live", matches[1], CancellationToken.None)).IsTrue();
        guard.Remove("live"); await Assert.That(await guard.HeartbeatAsync("live", matches[1], CancellationToken.None)).IsFalse();
    }
    private static HubCallerContext Context(Guid user)
    {
        var context = Substitute.For<HubCallerContext>(); context.ConnectionId.Returns("live"); context.UserIdentifier.Returns(user.ToString()); return context;
    }
    private static ILiveSoloConnectionAccess Access()
    {
        var access = Substitute.For<ILiveSoloConnectionAccess>();
        access.EligibleAsync(Arg.Any<IReadOnlyList<LiveSoloRealtimeAccessRequest>>(), CancellationToken.None)
            .Returns(call => (IReadOnlySet<string>)call.Arg<IReadOnlyList<LiveSoloRealtimeAccessRequest>>()!.Select(x => x.Key).ToHashSet()); return access;
    }
}
