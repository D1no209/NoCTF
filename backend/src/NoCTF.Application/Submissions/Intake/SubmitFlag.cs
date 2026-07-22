using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Accepts a Flag input fact and schedules asynchronous processing.</summary>
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

        var fingerprint = NoCTF.Domain.Submissions.FlagFingerprint.Create(command.Flag);
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var snapshot = await store.LoadAdmissionAsync(
                command.CompetitionId,
                command.TeamId,
                command.CompetitionChallengeId,
                command.UserId,
                cancellationToken);
            if (snapshot is null)
                return OperationResult<SubmissionAccepted>.Failure("submission_scope_not_found", "Submission scope was not found.");
            if (snapshot.Mode == NoCTF.Domain.Competitions.GameMode.Awd
                && (command.AttackTarget is null
                    || command.AttackTarget.TeamId == Guid.Empty
                    || command.AttackTarget.ServiceId == Guid.Empty))
                return OperationResult<SubmissionAccepted>.Failure(
                    "awd_attack_target_required", "AWD submissions require a target team and service.");
            if (snapshot.Mode != NoCTF.Domain.Competitions.GameMode.Awd && command.AttackTarget is not null)
                return OperationResult<SubmissionAccepted>.Failure(
                    "awd_attack_target_not_allowed", "Attack target dimensions are allowed only for AWD submissions.");
            if (snapshot.Mode == NoCTF.Domain.Competitions.GameMode.Penetration && command.StageId is null)
                return OperationResult<SubmissionAccepted>.Failure(
                    "penetration_stage_required", "Penetration submissions require a stage identifier.");
            if (snapshot.Mode == NoCTF.Domain.Competitions.GameMode.Penetration
                && snapshot.ChallengeInstanceId is null)
                return OperationResult<SubmissionAccepted>.Failure(
                    "penetration_instance_required", "A running Penetration challenge instance is required.");
            if (snapshot.Mode != NoCTF.Domain.Competitions.GameMode.Penetration && command.StageId is not null)
                return OperationResult<SubmissionAccepted>.Failure(
                    "penetration_stage_not_allowed", "Stage identifiers are allowed only for Penetration submissions.");

            var existing = await store.FindAcceptedAsync(
                command.CompetitionId, command.IdempotencyKey, command.TeamId, command.CompetitionChallengeId,
                command.UserId, NoCTF.Domain.Submissions.SubmissionKind.Flag, command.AttackTarget,
                command.StageId, snapshot.ChallengeInstanceId, fingerprint, cancellationToken);
            var existingResult = MapAcceptance(existing);
            if (existingResult is not null)
                return existingResult;

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
                CompetitionChallengeId = command.CompetitionChallengeId,
                UserId = command.UserId,
                FlagFingerprint = fingerprint,
                IdempotencyKey = command.IdempotencyKey,
                IpAddress = command.IpAddress,
                ReceivedAt = command.ReceivedAt,
                AttackTarget = command.AttackTarget,
                StageId = command.StageId,
                ChallengeInstanceId = snapshot.ChallengeInstanceId
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
        SubmissionAcceptanceState.BackgroundWorkUnavailable =>
            OperationResult<SubmissionAccepted>.Failure("background_work_unavailable", "Submission processing is temporarily unavailable."),
        _ => OperationResult<SubmissionAccepted>.Failure("submission_concurrency", "The submission could not be accepted.")
    };
}
