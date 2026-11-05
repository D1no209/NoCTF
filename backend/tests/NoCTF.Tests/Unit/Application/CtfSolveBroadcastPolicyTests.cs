using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Tests.Unit.Application;

public sealed class CtfSolveBroadcastPolicyTests
{
    [Test]
    [Arguments(GameMode.Ctf, true)]
    [Arguments(GameMode.Awd, false)]
    [Arguments(GameMode.Awdp, false)]
    [Arguments(GameMode.Koh, false)]
    [Arguments(GameMode.LiveSolo, false)]
    public async Task Blackout_broadcast_restriction_is_ctf_only(GameMode mode, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        await Assert.That(CtfSolveBroadcastPolicy.IsBlackout(mode, null, now, now)).IsEqualTo(expected);
        await Assert.That(CtfSolveBroadcastPolicy.IsBlackout(mode, now, null, now)).IsFalse();
        await Assert.That(CtfSolveBroadcastPolicy.IsBlackout(mode, null, now.AddTicks(1), now)).IsFalse();
    }
}
