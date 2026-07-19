using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionLifecyclePolicyTests
{
    [Test]
    public async Task ValidateTransition_AcceptsExpectedStateGraph()
    {
        var valid = new[]
        {
            (CompetitionStatus.Draft, CompetitionStatus.Published),
            (CompetitionStatus.Published, CompetitionStatus.Running),
            (CompetitionStatus.Running, CompetitionStatus.Paused),
            (CompetitionStatus.Paused, CompetitionStatus.Running),
            (CompetitionStatus.Paused, CompetitionStatus.Finished),
            (CompetitionStatus.Running, CompetitionStatus.Finished)
        };

        foreach (var (from, to) in valid)
            await Assert.That(CompetitionLifecyclePolicy.ValidateTransition(from, to).Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateTransition_RejectsFinishedAndSkippedStates()
    {
        await Assert.That(CompetitionLifecyclePolicy.ValidateTransition(CompetitionStatus.Finished, CompetitionStatus.Running).Succeeded).IsFalse();
        await Assert.That(CompetitionLifecyclePolicy.ValidateTransition(CompetitionStatus.Draft, CompetitionStatus.Running).Succeeded).IsFalse();
        await Assert.That(CompetitionLifecyclePolicy.ValidateTransition(CompetitionStatus.Published, CompetitionStatus.Paused).Succeeded).IsFalse();
    }

    [Test]
    public async Task ValidateSchedule_RequiresStartBeforeEnd()
    {
        var now = DateTimeOffset.UtcNow;
        await Assert.That(CompetitionLifecyclePolicy.ValidateSchedule(now, now).Succeeded).IsFalse();
        await Assert.That(CompetitionLifecyclePolicy.ValidateSchedule(now, now.AddMinutes(1)).Succeeded).IsTrue();
    }
}
