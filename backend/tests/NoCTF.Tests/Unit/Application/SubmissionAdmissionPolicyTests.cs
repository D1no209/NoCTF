using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class SubmissionAdmissionPolicyTests
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

        await Assert.That(result.FailureCode).IsEqualTo(SubmissionFailureCode.CompetitionFinished);
    }

    [Test]
    public async Task Banned_team_is_rejected_even_when_competition_is_running()
    {
        var snapshot = Snapshot(CompetitionStatus.Running) with { TeamBanned = true };

        var result = Check(snapshot, DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(SubmissionFailureCode.TeamBanned);
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

    private static SubmissionAdmissionSnapshot Snapshot(CompetitionStatus status) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GameMode.Ctf, 0, 0, "{}", "{}", 0, 0, status,
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddMinutes(1),
        false, false, true, false, false, true, true);

    private static NoCTF.Application.Common.OperationResult<SubmissionFailureCode> Check(
        SubmissionAdmissionSnapshot snapshot,
        DateTimeOffset receivedAt) => SubmissionAdmissionPolicy.Check(
        snapshot,
        NoCTF.Domain.Submissions.SubmissionKind.Flag,
        new(true, false, null, null),
        receivedAt);
}
