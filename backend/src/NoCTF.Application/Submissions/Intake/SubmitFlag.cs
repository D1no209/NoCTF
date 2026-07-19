using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Accepts a Flag into the permanent stream and schedules asynchronous processing.</summary>
public sealed class SubmitFlag(ISubmissionIntakeStore store, ISubmissionAdmissionModePolicy modePolicy)
{
    private const int MaxConcurrencyRetries = 3;

    public async Task<OperationResult<SubmissionAccepted>> ExecuteAsync(
        FlagSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Flag))
            return OperationResult<SubmissionAccepted>.Failure("flag_required", "Flag is required.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
            return OperationResult<SubmissionAccepted>.Failure("idempotency_key_invalid", "IdempotencyKey is required and cannot exceed 128 characters.");

        var existing = await store.FindAcceptedAsync(
            command.CompetitionId, command.IdempotencyKey, command.TeamId, command.ChallengeId,
            command.UserId, NoCTF.Domain.Submissions.SubmissionKind.Flag, cancellationToken);
        var existingResult = MapAcceptance(existing);
        if (existingResult is not null)
            return existingResult;

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

            var rules = modePolicy.GetRules(
                snapshot.Mode,
                snapshot.CompetitionConfigurationJson,
                snapshot.ChallengeConfigurationJson);
            var admission = SubmissionAdmissionPolicy.Check(
                snapshot,
                NoCTF.Domain.Submissions.SubmissionKind.Flag,
                rules,
                command.ReceivedAt);
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
                IdempotencyKey = command.IdempotencyKey,
                IpAddress = command.IpAddress,
                ReceivedAt = command.ReceivedAt
            };

            var accepted = await store.TryAcceptFlagAsync(received, snapshot, rules.MaxFlagAttempts, cancellationToken);
            if (accepted.State == SubmissionAcceptanceState.SnapshotChanged)
                continue;
            return MapAcceptance(accepted)!;
        }

        return OperationResult<SubmissionAccepted>.Failure("submission_concurrency", "The submission could not be accepted.");
    }

    private static OperationResult<SubmissionAccepted>? MapAcceptance(SubmissionAcceptanceResult? result) => result?.State switch
    {
        null => null,
        SubmissionAcceptanceState.Created or SubmissionAcceptanceState.Existing =>
            OperationResult<SubmissionAccepted>.Success(new(result.SubmissionId!.Value, result.ReceivedAt!.Value)),
        SubmissionAcceptanceState.IdempotencyConflict =>
            OperationResult<SubmissionAccepted>.Failure("idempotency_conflict", "IdempotencyKey is already bound to another submission scope."),
        SubmissionAcceptanceState.AttemptsExhausted =>
            OperationResult<SubmissionAccepted>.Failure("attempts_exhausted", "The maximum number of accepted attempts has been reached."),
        _ => OperationResult<SubmissionAccepted>.Failure("submission_concurrency", "The submission could not be accepted.")
    };
}
