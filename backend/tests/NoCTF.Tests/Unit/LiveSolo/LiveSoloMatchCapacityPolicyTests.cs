using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloMatchCapacityPolicyTests
{
    [Test, Arguments(LiveSoloMatchState.Preparing, false, false), Arguments(LiveSoloMatchState.Preparing, true, true),
        Arguments(LiveSoloMatchState.Countdown, false, true), Arguments(LiveSoloMatchState.Running, false, true),
        Arguments(LiveSoloMatchState.Paused, false, true), Arguments(LiveSoloMatchState.Paused, true, true),
        Arguments(LiveSoloMatchState.AwaitingAdjudication, true, true), Arguments(LiveSoloMatchState.AwaitingOpponents, false, false),
        Arguments(LiveSoloMatchState.Completed, true, false), Arguments(LiveSoloMatchState.Canceled, true, false)]
    public async Task Reservations_cover_initial_paused_countdown_and_between_rounds_until_the_match_ends(
        LiveSoloMatchState state, bool started, bool expected)
    {
        var match = new LiveSoloMatch { State = state, StartedAt = started ? DateTimeOffset.UtcNow : null };
        await Assert.That(LiveSoloMatchCapacityPolicy.OccupiesSlot.Compile()(match)).IsEqualTo(expected);
    }
    [Test]
    public async Task Countdown_and_resume_use_the_same_limit_for_other_reserved_matches()
    {
        await Assert.That(LiveSoloMatchCapacityPolicy.CanEnter(0, 1)).IsTrue();
        await Assert.That(LiveSoloMatchCapacityPolicy.CanEnter(1, 1)).IsFalse();
        await Assert.That(LiveSoloMatchCapacityPolicy.CanEnter(2, 1)).IsFalse();
    }
}
