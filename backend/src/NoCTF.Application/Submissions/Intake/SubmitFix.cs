using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public sealed class SubmitFix(ISubmissionIntakeStore store, ISubmissionAdmissionModePolicy modePolicy)
{
    public async Task<OperationResult<SubmissionAccepted, SubmissionFailureCode>> ExecuteAsync(
        FixSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId, command.CompetitionChallengeId, command.UserId, cancellationToken);
        if (snapshot is null)
            return OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                SubmissionFailureCode.SubmissionScopeNotFound, "Submission scope was not found.");
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson);
        var admission = SubmissionAdmissionPolicy.Check(
            snapshot, SubmissionKind.Fix, rules, command.ReceivedAt);
        if (!admission.Succeeded)
            return OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                admission.FailureCode!.Value, admission.ErrorMessage!);

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
                OperationResult<SubmissionAccepted, SubmissionFailureCode>.Success(new(accepted.SubmissionId!.Value, accepted.ReceivedAt!.Value)),
            SubmissionAcceptanceState.PatchUploadUnavailable =>
                OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                    SubmissionFailureCode.PatchUploadNotFound, "The patch upload was not found or has already been consumed."),
            SubmissionAcceptanceState.AttemptsExhausted =>
                OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                    SubmissionFailureCode.AttemptsExhausted, "The maximum number of accepted attempts has been reached."),
            _ => OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                SubmissionFailureCode.SubmissionConcurrency, "The submission could not be accepted because its scope changed.")
        };
    }
}
