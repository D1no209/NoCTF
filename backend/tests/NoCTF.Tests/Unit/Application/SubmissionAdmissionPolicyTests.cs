using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class SubmissionAdmissionPolicyTests
{
    [Test]
    public async Task Submission_received_before_end_remains_valid_after_processing_delay()
    {
        var snapshot = Snapshot(CompetitionStatus.Running);
        var receivedAt = snapshot.EndTime.AddSeconds(-1);

        var result = SubmissionAdmissionPolicy.Check(snapshot, receivedAt);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Submission_received_after_end_is_rejected()
    {
        var snapshot = Snapshot(CompetitionStatus.Running);

        var result = SubmissionAdmissionPolicy.Check(snapshot, snapshot.EndTime.AddTicks(1));

        await Assert.That(result.ErrorCode).IsEqualTo("competition_finished");
    }

    [Test]
    public async Task Banned_team_is_rejected_even_when_competition_is_running()
    {
        var snapshot = Snapshot(CompetitionStatus.Running) with { TeamBanned = true };

        var result = SubmissionAdmissionPolicy.Check(snapshot, DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("team_banned");
    }

    private static SubmissionAdmissionSnapshot Snapshot(CompetitionStatus status) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, status,
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddMinutes(1),
        false, false, true, false, false, true, true);
}
