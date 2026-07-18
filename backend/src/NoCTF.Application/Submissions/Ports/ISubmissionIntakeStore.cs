using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;

namespace NoCTF.Application.Submissions.Ports;

/// <summary>Provides the concurrency-aware transaction seam for accepting submissions.</summary>
public interface ISubmissionIntakeStore
{
    Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        long expectedRevision,
        CancellationToken cancellationToken);

    Task<bool> TryAcceptFixAsync(
        FixSubmissionReceived received,
        long expectedRevision,
        CancellationToken cancellationToken);
}
