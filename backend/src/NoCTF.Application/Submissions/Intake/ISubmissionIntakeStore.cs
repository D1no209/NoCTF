using NoCTF.Application.Submissions.Intake;

namespace NoCTF.Application.Submissions.Intake;

public interface ISubmissionIntakeStore
{
    Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubmissionAcceptanceResult>> TryAcceptFlagsAsync(
        IReadOnlyList<FlagSubmissionReceived> received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
        FixSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<SubmissionAcceptanceResult> TryAcceptHintUnlockAsync(
        HintUnlockSubmissionReceived received,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged));

    Task<SubmissionAcceptanceResult> TryAcceptManualAdjustmentAsync(
        ManualAdjustmentSubmissionReceived received,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged));
}
