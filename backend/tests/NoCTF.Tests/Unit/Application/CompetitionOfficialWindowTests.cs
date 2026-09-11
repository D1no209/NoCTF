using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionOfficialWindowTests
{
    [Test]
    public async Task Resolve_without_finish_event_uses_scheduled_end()
    {
        var start = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var end = start.AddHours(2);

        var window = CompetitionOfficialWindow.Resolve(start, end, []);

        await Assert.That(window).IsEqualTo(new CompetitionOfficialWindow(start, end));
    }

    [Test]
    public async Task Resolve_uses_early_terminal_transition()
    {
        var start = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var end = start.AddHours(2);
        var finishedAt = start.AddMinutes(30);

        var window = CompetitionOfficialWindow.Resolve(start, end,
        [
            new CompetitionLifecycleTransition
            {
                From = CompetitionStatus.Published,
                To = CompetitionStatus.Running,
                OccurredAt = start
            },
            new CompetitionLifecycleTransition
            {
                From = CompetitionStatus.Running,
                To = CompetitionStatus.Finished,
                OccurredAt = finishedAt
            }
        ]);

        await Assert.That(window.EndAt).IsEqualTo(finishedAt);
    }

    [Test]
    public async Task Contains_uses_a_half_open_interval()
    {
        var start = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var end = start.AddHours(2);
        var window = new CompetitionOfficialWindow(start, end);

        await Assert.That(window.Contains(start)).IsTrue();
        await Assert.That(window.Contains(end.AddTicks(-1))).IsTrue();
        await Assert.That(window.Contains(end)).IsFalse();
    }
}
