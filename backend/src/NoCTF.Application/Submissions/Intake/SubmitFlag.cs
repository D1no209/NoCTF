using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Accepts a Flag into the permanent stream and schedules asynchronous processing.</summary>
public sealed class SubmitFlag(ISubmissionIntakeStore store)
{
    private const int MaxConcurrencyRetries = 3;

    public async Task<OperationResult<SubmissionAccepted>> ExecuteAsync(
        FlagSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Flag))
            return OperationResult<SubmissionAccepted>.Failure("flag_required", "Flag is required.");

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
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
            var received = new FlagSubmissionReceived
            {
                SubmissionId = submissionId,
                CompetitionId = command.CompetitionId,
                TeamId = command.TeamId,
                ChallengeId = command.ChallengeId,
                UserId = command.UserId,
                Flag = command.Flag,
                IpAddress = command.IpAddress,
                ReceivedAt = command.ReceivedAt
            };

            if (!await store.TryAcceptFlagAsync(received, snapshot.Revision, cancellationToken))
                continue;

            return OperationResult<SubmissionAccepted>.Success(new(submissionId, command.ReceivedAt));
        }

        return OperationResult<SubmissionAccepted>.Failure("submission_concurrency", "The submission could not be accepted.");
    }
}
