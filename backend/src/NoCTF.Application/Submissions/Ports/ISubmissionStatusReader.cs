using NoCTF.Application.Submissions.Events;

namespace NoCTF.Application.Submissions.Ports;

public sealed record SubmissionStatusView(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    SubmissionKind Kind,
    SubmissionOutcome Outcome,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode);

public interface ISubmissionStatusReader
{
    Task<SubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, Guid userId, CancellationToken cancellationToken);
}
