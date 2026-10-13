using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Scheduling;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdRoundTimelineTests
{
    [Test]
    public async Task Duration_limit_excludes_gaps_and_keeps_a_partial_final_round()
    {
        var first = Round(1, 60, 120, 30);
        var second = Round(2, 120, 180, 30);
        var plan = AwdRoundTimeline.Plan([first, second], TimeSpan.FromSeconds(240),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), 30, activeTimeLimit: TimeSpan.FromSeconds(185));
        var last = plan.NewRounds.Single();
        await Assert.That(last.Partial).IsTrue();
        await Assert.That(last.ActiveEndTicks).IsEqualTo(TimeSpan.FromSeconds(185).Ticks);
        await Assert.That(last.RunningEndTicks).IsEqualTo(TimeSpan.FromSeconds(245).Ticks);
        await Assert.That(AwdRoundTimeline.EndPosition([first, second], TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60), 30, null, TimeSpan.FromSeconds(185))).IsEqualTo(TimeSpan.FromSeconds(245));
        var closed = AwdRoundTimeline.Plan([first, second], TimeSpan.FromSeconds(500),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), 30, activeTimeLimit: TimeSpan.FromSeconds(185));
        await Assert.That(closed.NewRounds.Single().Missed).IsTrue();
        await Assert.That(closed.NextRunningPosition).IsNull();
    }

    [Test]
    public async Task Round_limit_ignores_duration_budget_and_never_adds_a_final_gap()
    {
        var end = AwdRoundTimeline.EndPosition([Round(1, 60, 120, 30)], TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60), 20, 3, TimeSpan.FromSeconds(125));
        await Assert.That(end).IsEqualTo(TimeSpan.FromSeconds(290));
    }

    [Test]
    public async Task Gaps_freeze_active_time_and_boundaries_start_the_next_round()
    {
        var first = Round(1, 60, 240, 30);
        var gap = Plan([first], 250);
        await Assert.That(gap.Current).IsNull();
        await Assert.That(gap.InGap).IsTrue();
        await Assert.That(gap.ActiveTicks).IsEqualTo(TimeSpan.FromSeconds(240).Ticks);
        await Assert.That(gap.NextRunningPosition).IsEqualTo(TimeSpan.FromSeconds(270));
        var next = Plan([first], 270);
        await Assert.That(next.NewRounds.Single().Number).IsEqualTo(2);
        await Assert.That(next.NewRounds.Single().ActiveStartTicks).IsEqualTo(TimeSpan.FromSeconds(240).Ticks);
        await Assert.That(next.NewRounds.Single().RunningStartTicks).IsEqualTo(TimeSpan.FromSeconds(270).Ticks);
    }

    [Test]
    public async Task Takeover_records_missed_rounds_without_relabeling_the_current_round()
    {
        var result = Plan([Round(1, 60, 240, 30)], 730);
        await Assert.That(result.NewRounds.Select(round => round.Number).ToArray()).IsEquivalentTo(new[] { 2, 3, 4 });
        await Assert.That(result.NewRounds.Count(round => round.Missed)).IsEqualTo(2);
        await Assert.That(result.NewRounds[^1].Missed).IsFalse();
        await Assert.That(result.ActiveTicks).IsEqualTo(TimeSpan.FromSeconds(640).Ticks);
    }

    [Test]
    public async Task Duration_changes_preserve_saved_rounds_and_apply_to_the_successor()
    {
        var first = Round(1, 60, 240, 30);
        var current = AwdRoundTimeline.Plan([first], TimeSpan.FromSeconds(200), TimeSpan.Zero,
            TimeSpan.FromSeconds(300), 60);
        await Assert.That(current.Current).IsEqualTo(first);
        await Assert.That(current.NewRounds).IsEmpty();
        var next = AwdRoundTimeline.Plan([first], TimeSpan.FromSeconds(270), TimeSpan.Zero,
            TimeSpan.FromSeconds(300), 60);
        await Assert.That(next.NewRounds.Single().ActiveEndTicks).IsEqualTo(TimeSpan.FromSeconds(540).Ticks);
        await Assert.That(next.NewRounds.Single().GapSeconds).IsEqualTo(60);
    }

    [Test]
    public async Task Hardening_and_total_round_limit_do_not_create_extra_rounds()
    {
        var waiting = Plan([], 59);
        await Assert.That(waiting.NewRounds).IsEmpty();
        await Assert.That(waiting.NextRunningPosition).IsEqualTo(TimeSpan.FromSeconds(60));
        var finished = AwdRoundTimeline.Plan([Round(1, 60, 240, 0)], TimeSpan.FromSeconds(240),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180), 0, 1);
        await Assert.That(finished.NewRounds).IsEmpty();
        await Assert.That(finished.NextRunningPosition).IsNull();
    }

    private static AwdTimelinePlan Plan(AwdRound[] rounds, int seconds) => AwdRoundTimeline.Plan(
        rounds, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180), 30);

    private static AwdRound Round(int number, int start, int end, int gap) => new()
    {
        Number = number, ActiveStartTicks = TimeSpan.FromSeconds(start).Ticks,
        ActiveEndTicks = TimeSpan.FromSeconds(end).Ticks, GapSeconds = gap
    };
}
