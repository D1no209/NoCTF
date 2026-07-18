using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public class AwdpScoringRulesTests
{
    [Test]
    public async Task Platform_failure_does_not_consume_team_attempt()
    {
        await Assert.That(AwdpScoringRules.ConsumesAttempt(true, true)).IsFalse();
    }

    [Test]
    public async Task Team_failure_consumes_attempt()
    {
        await Assert.That(AwdpScoringRules.ConsumesAttempt(true, false)).IsTrue();
    }
}
