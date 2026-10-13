using NoCTF.GameModes.Awd.Scheduling;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdRoundSchedulerTests
{
    [Test]
    public async Task InjectionRetry_UsesBoundedExponentialDelayAndStopsAtWindowEnd()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var validUntil = now.AddSeconds(20);

        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, validUntil, 0))
            .IsEqualTo(now.AddSeconds(1));
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, validUntil, 4))
            .IsEqualTo(now.AddSeconds(16));
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, validUntil, 5))
            .IsNull();
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(now, now.AddMinutes(2), 8))
            .IsEqualTo(now.AddSeconds(30));
        await Assert.That(AwdRoundScheduler.NextInjectionAttempt(validUntil, validUntil, 0))
            .IsNull();
    }

}
