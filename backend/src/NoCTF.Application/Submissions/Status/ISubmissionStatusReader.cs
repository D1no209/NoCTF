using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Status;

public sealed record SubmissionStatusView(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    SubmissionKind Kind,
    SubmissionEvaluationState EvaluationState,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset EvaluationUpdatedAt,
    long ProcessingVersion);

public interface ISubmissionStatusReader
{
    Task<SubmissionStatusView?> FindAsync(
        Guid competitionId,
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken);
}
