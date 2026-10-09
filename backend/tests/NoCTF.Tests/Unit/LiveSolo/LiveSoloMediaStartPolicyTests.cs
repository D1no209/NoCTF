using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloMediaStartPolicyTests
{
    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(10, true)]
    [Arguments(11, false)]
    public async Task Start_proof_rejects_future_or_expired_observations(int ageSeconds, bool expected)
    {
        var at = DateTimeOffset.UtcNow;
        await Assert.That(LiveSoloMediaPolicy.FreshStartProof(at, at.AddSeconds(ageSeconds))).IsEqualTo(expected);
    }
}
