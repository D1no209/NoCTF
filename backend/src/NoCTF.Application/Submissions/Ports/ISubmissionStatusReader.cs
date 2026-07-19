using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Ports;

public sealed record SubmissionStatusView(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    SubmissionKind Kind,
    ScoringResult? Result,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    ScoringFailureCode? FailureCode,
    string? EvaluatorVersion);

public interface ISubmissionStatusReader
{
    Task<SubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, Guid userId, CancellationToken cancellationToken);
}
