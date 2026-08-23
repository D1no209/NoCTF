using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionEffectiveRuntimeTests
{
    [Test]
    public async Task Running_intervals_are_accumulated_without_paused_time()
    {
        var start = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        var transitions = new[]
        {
            Moment(1, start, CompetitionStatus.Published, CompetitionStatus.Running),
            Moment(2, start.AddMinutes(10), CompetitionStatus.Running, CompetitionStatus.Paused),
            Moment(3, start.AddMinutes(20), CompetitionStatus.Paused, CompetitionStatus.Running),
            Moment(4, start.AddMinutes(35), CompetitionStatus.Running, CompetitionStatus.Finished)
        };

        var result = CompetitionEffectiveRuntimePolicy.Calculate(
            start,
            start.AddHours(1),
            start.AddMinutes(50),
            transitions);

        await Assert.That(result.Elapsed).IsEqualTo(TimeSpan.FromMinutes(25));
        await Assert.That(result.RunningSince).IsNull();
    }

    [Test]
    public async Task Current_running_interval_is_capped_by_now_and_ignores_future_transitions()
    {
        var start = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        var transitions = new[]
        {
            Moment(1, start, CompetitionStatus.Published, CompetitionStatus.Running),
            Moment(2, start.AddMinutes(20), CompetitionStatus.Running, CompetitionStatus.Paused)
        };

        var result = CompetitionEffectiveRuntimePolicy.Calculate(
            start,
            start.AddHours(1),
            start.AddMinutes(5),
            transitions);

        await Assert.That(result.Elapsed).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(result.RunningSince).IsEqualTo(start);
    }

    [Test]
    public async Task Current_running_interval_is_capped_by_competition_end()
    {
        var start = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        var result = CompetitionEffectiveRuntimePolicy.Calculate(
            start,
            start.AddMinutes(30),
            start.AddHours(2),
            [Moment(1, start.AddMinutes(10), CompetitionStatus.Published, CompetitionStatus.Running)]);

        await Assert.That(result.Elapsed).IsEqualTo(TimeSpan.FromMinutes(20));
        await Assert.That(result.RunningSince).IsNull();
    }

    [Test]
    public async Task Events_with_same_timestamp_are_ordered_by_event_id()
    {
        var start = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        var instant = start.AddMinutes(10);
        var result = CompetitionEffectiveRuntimePolicy.Calculate(
            start,
            start.AddHours(1),
            start.AddMinutes(20),
            [
                Moment(2, instant, CompetitionStatus.Paused, CompetitionStatus.Running),
                Moment(1, instant, CompetitionStatus.Running, CompetitionStatus.Paused)
            ]);

        await Assert.That(result.Elapsed).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(result.RunningSince).IsEqualTo(instant);
    }

    private static CompetitionLifecycleMoment Moment(
        byte idSuffix,
        DateTimeOffset occurredAt,
        CompetitionStatus from,
        CompetitionStatus to)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes[15] = idSuffix;
        return new(new Guid(bytes), occurredAt, from, to);
    }
}
