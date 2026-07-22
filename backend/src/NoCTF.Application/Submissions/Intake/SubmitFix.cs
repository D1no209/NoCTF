using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Accepts an uploaded Fix reference and schedules isolated asynchronous validation.</summary>
public sealed class SubmitFix(
    ISubmissionIntakeStore store,
    NoCTF.Application.Storage.IFixUploadSessionStore uploads,
    ISubmissionAdmissionModePolicy modePolicy)
{
    public async Task<OperationResult<SubmissionAccepted>> ExecuteAsync(
        FixSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
            return OperationResult<SubmissionAccepted>.Failure("idempotency_key_invalid", "IdempotencyKey is required and cannot exceed 128 characters.");

        var existing = await store.FindAcceptedAsync(
            command.CompetitionId, command.IdempotencyKey, command.TeamId, command.CompetitionChallengeId,
            command.UserId, NoCTF.Domain.Submissions.SubmissionKind.Fix, null, null, null, null, cancellationToken);
        if (existing is not null)
            return MapAcceptance(existing);

        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId,
            command.TeamId,
            command.CompetitionChallengeId,
            command.UserId,
            cancellationToken);
        if (snapshot is null)
            return OperationResult<SubmissionAccepted>.Failure("submission_scope_not_found", "Submission scope was not found.");

        var rules = modePolicy.GetRules(
            snapshot.Mode,
            snapshot.CompetitionConfigurationJson,
            snapshot.ChallengeConfigurationJson);
        var admission = SubmissionAdmissionPolicy.Check(
            snapshot,
            NoCTF.Domain.Submissions.SubmissionKind.Fix,
            rules,
            command.ReceivedAt);
        if (!admission.Succeeded)
            return OperationResult<SubmissionAccepted>.Failure(admission.ErrorCode!, admission.ErrorMessage!);

        var submissionId = Guid.CreateVersion7(command.ReceivedAt);
        var metadata = await uploads.GetAuthorizedMetadataAsync(
            command.UploadId,
            command.CompetitionId,
            command.TeamId,
            command.CompetitionChallengeId,
            command.UserId,
            command.ReceivedAt,
            cancellationToken);
        if (metadata is null)
            return OperationResult<SubmissionAccepted>.Failure("upload_not_found", "The upload was not found or has expired.");

        var received = new FixSubmissionReceived(
            submissionId,
            command.CompetitionId,
            command.TeamId,
            command.CompetitionChallengeId,
            command.UserId,
            command.UploadId,
            command.IdempotencyKey,
            new(metadata.ObjectKey, metadata.FileName, metadata.ContentType, metadata.Length, metadata.Sha256),
            command.IpAddress,
            command.ReceivedAt);

        var accepted = await store.TryAcceptFixAsync(received, snapshot, rules.MaxFixAttempts, cancellationToken);
        return MapAcceptance(accepted);
    }

    private static OperationResult<SubmissionAccepted> MapAcceptance(SubmissionAcceptanceResult result) => result.State switch
    {
        SubmissionAcceptanceState.Created or SubmissionAcceptanceState.Existing =>
            OperationResult<SubmissionAccepted>.Success(new(result.SubmissionId!.Value, result.ReceivedAt!.Value)),
        SubmissionAcceptanceState.IdempotencyConflict =>
            OperationResult<SubmissionAccepted>.Failure("idempotency_conflict", "IdempotencyKey is already bound to another submission scope."),
        SubmissionAcceptanceState.AttemptsExhausted =>
            OperationResult<SubmissionAccepted>.Failure("attempts_exhausted", "The maximum number of accepted attempts has been reached."),
        SubmissionAcceptanceState.UploadUnavailable =>
            OperationResult<SubmissionAccepted>.Failure("upload_not_found", "The upload was not found or has expired."),
        SubmissionAcceptanceState.BackgroundWorkUnavailable =>
            OperationResult<SubmissionAccepted>.Failure("background_work_unavailable", "Submission processing is temporarily unavailable."),
        _ => OperationResult<SubmissionAccepted>.Failure("submission_concurrency", "The submission could not be accepted.")
    };
}
