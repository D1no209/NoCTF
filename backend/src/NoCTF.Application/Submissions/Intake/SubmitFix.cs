using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Accepts an uploaded Fix reference and schedules isolated asynchronous validation.</summary>
public sealed class SubmitFix(ISubmissionIntakeStore store, ISubmissionQueue queue)
{
    public async Task<OperationResult<SubmissionAccepted>> ExecuteAsync(
        FixSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId,
            command.TeamId,
            command.ChallengeId,
            command.UserId,
            cancellationToken);
        if (snapshot is null)
            return OperationResult<SubmissionAccepted>.Failure("submission_scope_not_found", "Submission scope was not found.");

        var admission = SubmissionAdmissionPolicy.Check(snapshot, command.ReceivedAt);
        if (!admission.Succeeded)
            return OperationResult<SubmissionAccepted>.Failure(admission.ErrorCode!, admission.ErrorMessage!);

        var submissionId = Guid.CreateVersion7(command.ReceivedAt);
        var received = new FixSubmissionReceived(
            submissionId,
            command.CompetitionId,
            command.TeamId,
            command.ChallengeId,
            command.UserId,
            command.Archive,
            command.IpAddress,
            command.ReceivedAt);
        if (!await store.TryAcceptFixAsync(received, snapshot.Revision, cancellationToken))
            return OperationResult<SubmissionAccepted>.Failure("submission_concurrency", "The submission could not be accepted.");

        await queue.EnqueueAsync(
            new ProcessFixSubmission(command.CompetitionId, command.TeamId, command.UserId, submissionId),
            cancellationToken);
        return OperationResult<SubmissionAccepted>.Success(new(submissionId, command.ReceivedAt));
    }
}
