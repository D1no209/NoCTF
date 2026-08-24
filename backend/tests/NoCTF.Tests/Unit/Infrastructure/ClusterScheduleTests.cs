using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class ClusterScheduleTests
{
    [Test]
    public async Task Clamp_skips_past_ticks_without_replaying_them()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var intended = now.AddSeconds(-35);

        var result = ClusterScheduleClock.ClampWithoutCatchUp(
            intended,
            now,
            TimeSpan.FromSeconds(10));

        await Assert.That(result.DueAt).IsEqualTo(now);
        await Assert.That(result.SkippedTicks).IsEqualTo(3);
        await Assert.That(ClusterScheduleClock.NextAfterDispatch(
            now,
            TimeSpan.FromSeconds(10))).IsEqualTo(now.AddSeconds(10));
    }

    [Test]
    public async Task Moving_a_KoH_tick_changes_its_stable_fact_key()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var dueAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var first = PollKohChallenge.Create(
            competitionId,
            challengeId,
            dueAt.AddMinutes(-5),
            dueAt);

        var replay = PollKohChallenge.Create(
            competitionId,
            challengeId,
            dueAt.AddMinutes(-5),
            dueAt);
        var next = first.At(dueAt.AddSeconds(10));

        await Assert.That(replay.GameplayFactId).IsEqualTo(first.GameplayFactId);
        await Assert.That(next.GameplayFactId).IsNotEqualTo(first.GameplayFactId);
        await Assert.That(next.DueAt).IsEqualTo(dueAt.AddSeconds(10));
    }
}
