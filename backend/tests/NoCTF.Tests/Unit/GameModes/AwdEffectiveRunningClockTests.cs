using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Scheduling;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdEffectiveRunningClockTests
{
    [Test]
    public async Task Paused_time_does_not_advance_the_effective_clock()
    {
        var start = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var audits = new[]
        {
            Audit(CompetitionStatus.Published, CompetitionStatus.Running, start),
            Audit(CompetitionStatus.Running, CompetitionStatus.Paused, start.AddMinutes(2)),
            Audit(CompetitionStatus.Paused, CompetitionStatus.Running, start.AddMinutes(12))
        };

        var effective = AwdEffectiveRunningClock.Calculate(audits, start.AddMinutes(13));

        await Assert.That(effective).IsEqualTo(TimeSpan.FromMinutes(3));
    }

    private static CompetitionLifecycleTransition Audit(
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            From = from,
            To = to,
            OccurredAt = at
        };
}
