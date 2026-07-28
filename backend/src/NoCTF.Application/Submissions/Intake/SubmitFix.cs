using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public sealed class SubmitFix(ISubmissionIntakeStore store, ISubmissionAdmissionModePolicy modePolicy)
{
    public async Task<OperationResult<SubmissionAccepted>> ExecuteAsync(
        FixSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId, command.CompetitionChallengeId, command.UserId, cancellationToken);
        if (snapshot is null)
            return OperationResult<SubmissionAccepted>.Failure(
                "submission_scope_not_found", "Submission scope was not found.");
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson);
        var admission = SubmissionAdmissionPolicy.Check(
            snapshot, SubmissionKind.Fix, rules, command.ReceivedAt);
        if (!admission.Succeeded)
            return OperationResult<SubmissionAccepted>.Failure(admission.ErrorCode!, admission.ErrorMessage!);

        var submissionId = Guid.CreateVersion7(command.ReceivedAt);
        var accepted = await store.TryAcceptFixAsync(
            new(
                submissionId,
                command.CompetitionId,
                snapshot.TeamId,
                command.CompetitionChallengeId,
                command.UserId,
                command.PatchUploadId,
                command.ReceivedAt),
            snapshot,
            rules.MaxFixAttempts,
            cancellationToken);
        return accepted.State switch
        {
            SubmissionAcceptanceState.Created =>
                OperationResult<SubmissionAccepted>.Success(new(accepted.SubmissionId!.Value, accepted.ReceivedAt!.Value)),
            SubmissionAcceptanceState.PatchUploadUnavailable =>
                OperationResult<SubmissionAccepted>.Failure(
                    "patch_upload_not_found", "The patch upload was not found or has already been consumed."),
            SubmissionAcceptanceState.AttemptsExhausted =>
                OperationResult<SubmissionAccepted>.Failure(
                    "attempts_exhausted", "The maximum number of accepted attempts has been reached."),
            _ => OperationResult<SubmissionAccepted>.Failure(
                "submission_concurrency", "The submission could not be accepted because its scope changed.")
        };
    }
}
