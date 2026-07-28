using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Status;

public sealed record AdminSubmissionStatusView(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid SubmittedByUserId,
    SubmissionKind Kind,
    SubmissionEvaluationState EvaluationState,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset EvaluationUpdatedAt,
    long ProcessingVersion);

public interface IAdminSubmissionStatusReader
{
    Task<AdminSubmissionStatusView?> FindAsync(
        Guid competitionId,
        Guid submissionId,
        CancellationToken cancellationToken);
}
