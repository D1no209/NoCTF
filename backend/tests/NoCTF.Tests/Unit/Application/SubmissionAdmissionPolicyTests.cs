using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public class GameplayFactAdmissionPolicyTests
{
    [Test]
    public async Task Submission_received_before_end_remains_valid_after_processing_delay()
    {
        var snapshot = Snapshot(CompetitionStatus.Running);
        var receivedAt = snapshot.EndAt.AddSeconds(-1);

        var result = Check(snapshot, receivedAt);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Submission_received_after_end_is_rejected()
    {
        var snapshot = Snapshot(CompetitionStatus.Running);

        var result = Check(snapshot, snapshot.EndAt.AddTicks(1));

        await Assert.That(result.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.CompetitionFinished);
    }

    [Test]
    public async Task Banned_team_is_rejected_even_when_competition_is_running()
    {
        var snapshot = Snapshot(CompetitionStatus.Running) with { TeamBanned = true };

        var result = Check(snapshot, DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.TeamBanned);
    }

    [Test]
    public async Task Pending_team_is_rejected_even_when_competition_is_running()
    {
        var snapshot = Snapshot(CompetitionStatus.Running) with { TeamApproved = false };

        var result = Check(snapshot, DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactAdmissionFailureCode.TeamForbidden);
    }

    [Test]
    public async Task NonRunning_competitions_are_rejected()
    {
        foreach (var status in new[] { CompetitionStatus.Draft, CompetitionStatus.Published, CompetitionStatus.Paused, CompetitionStatus.Finished })
        {
            var result = Check(Snapshot(status), DateTimeOffset.UtcNow);
            await Assert.That(result.Succeeded).IsFalse();
        }
    }

    [Test]
    [Arguments(GameplayFactKind.BreakAttempt, true, false)]
    [Arguments(GameplayFactKind.FixAttempt, false, true)]
    public async Task Successful_AWDP_achievement_rejects_later_attempts(
        GameplayFactKind kind,
        bool hasCorrectBreak,
        bool hasCorrectFix)
    {
        var snapshot = Snapshot(CompetitionStatus.Running) with
        {
            Mode = GameMode.Awdp,
            HasCorrectBreak = hasCorrectBreak,
            HasCorrectFix = hasCorrectFix
        };

        var result = GameplayFactAdmissionPolicy.Check(
            snapshot,
            kind,
            new(true, true, null, null),
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactAdmissionFailureCode.AchievementAlreadySucceeded);
    }

    private static GameplayFactAdmissionSnapshot Snapshot(CompetitionStatus status) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GameMode.Ctf, "{}", "{}", 0, 0, status,
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddMinutes(1),
        false, false, true, false, false, true, true);

    private static NoCTF.Application.Common.OperationResult<GameplayFactAdmissionFailureCode> Check(
        GameplayFactAdmissionSnapshot snapshot,
        DateTimeOffset receivedAt) => GameplayFactAdmissionPolicy.Check(
        snapshot,
        NoCTF.Domain.Gameplay.GameplayFactKind.FlagAttempt,
        new(true, false, null, null),
        receivedAt);
}
