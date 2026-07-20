using NoCTF.Application.Submissions.Intake;

namespace NoCTF.Application.Submissions.Ports;

/// <summary>Provides the concurrency-aware transaction seam for accepting submissions.</summary>
public interface ISubmissionIntakeStore
{
    Task<SubmissionAcceptanceResult?> FindAcceptedAsync(
        Guid competitionId,
        string idempotencyKey,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        NoCTF.Domain.Submissions.SubmissionKind kind,
        AwdAttackTarget? attackTarget,
        CancellationToken cancellationToken);

    Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
        FixSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);
}
