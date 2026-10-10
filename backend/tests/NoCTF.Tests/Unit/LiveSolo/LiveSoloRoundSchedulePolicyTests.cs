using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloRoundSchedulePolicyTests
{
    [Test]
    public async Task Runtime_quota_must_cover_all_environments_that_remain_open_until_round_end()
    {
        await Assert.That(LiveSoloRoundSchedulePolicy.FitsRuntimeQuota(2, 1)).IsFalse();
        await Assert.That(LiveSoloRoundSchedulePolicy.FitsRuntimeQuota(2, 2)).IsTrue();
        await Assert.That(LiveSoloRoundSchedulePolicy.FitsRuntimeQuota(64, 0)).IsTrue();
    }
    [Test]
    public async Task Failed_readiness_after_countdown_uses_bounded_recovery_instead_of_an_immediate_spin()
    {
        var now = DateTimeOffset.UtcNow;
        var round = new LiveSoloRound { State = LiveSoloRoundState.Countdown, CountdownAt = now.AddSeconds(-10), CountdownSeconds = 5,
            Questions = [new() { Position = 0, Readiness = LiveSoloQuestionReadiness.Failed }] };
        await Assert.That(LiveSoloRoundSchedulePolicy.NextWakeup(round, now)).IsEqualTo(now.AddSeconds(2));
    }
    [Test]
    public async Task Preparation_is_limited_to_the_first_or_next_due_question_and_wakeups_use_the_timeline()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new LiveSoloRoundQuestion { Id = Guid.NewGuid(), Position = 0, OpenOffsetSeconds = 0, Readiness = LiveSoloQuestionReadiness.Ready };
        var second = new LiveSoloRoundQuestion { Id = Guid.NewGuid(), Position = 1, OpenOffsetSeconds = 180 };
        var third = new LiveSoloRoundQuestion { Id = Guid.NewGuid(), Position = 2, OpenOffsetSeconds = 360 };
        var round = new LiveSoloRound { State = LiveSoloRoundState.Preparing, LimitSeconds = 900, Questions = [first, second, third] };
        await Assert.That(LiveSoloRoundSchedulePolicy.PreparationCandidates(round, now)).IsEquivalentTo([first.Id]);
        await Assert.That(LiveSoloRoundSchedulePolicy.NextWakeup(round, now)).IsNull();
        round.State = LiveSoloRoundState.Running; round.StartedAt = now; first.OpenedAt = now;
        await Assert.That(LiveSoloRoundSchedulePolicy.PreparationCandidates(round, now.AddSeconds(149))).IsEmpty();
        await Assert.That(LiveSoloRoundSchedulePolicy.NextWakeup(round, now)).IsEqualTo(now.AddSeconds(150));
        await Assert.That(LiveSoloRoundSchedulePolicy.PreparationCandidates(round, now.AddSeconds(150))).IsEquivalentTo([second.Id]);
        second.Readiness = LiveSoloQuestionReadiness.Ready;
        await Assert.That(LiveSoloRoundSchedulePolicy.NextWakeup(round, now.AddSeconds(150))).IsEqualTo(now.AddSeconds(180));
        await Assert.That(LiveSoloRoundSchedulePolicy.PreparationCandidates(round, now.AddSeconds(900))).IsEmpty();
    }
}
