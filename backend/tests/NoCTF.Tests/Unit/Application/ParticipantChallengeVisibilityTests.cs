using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class ParticipantChallengeVisibilityTests
{
    [Test]
    [Arguments(CompetitionStatus.Draft)]
    [Arguments(CompetitionStatus.Visible)]
    [Arguments(CompetitionStatus.Published)]
    public async Task Challenges_are_hidden_before_competition_starts(
        CompetitionStatus status) =>
        await Assert.That(ParticipantChallengeVisibilityPolicy.CanView(status)).IsFalse();

    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    [Arguments(CompetitionStatus.Finished)]
    public async Task Challenges_remain_visible_after_competition_starts(
        CompetitionStatus status) =>
        await Assert.That(ParticipantChallengeVisibilityPolicy.CanView(status)).IsTrue();
}
