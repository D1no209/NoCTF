using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Ports;

public sealed record AdminSubmissionStatusView(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid? TeamId,
    Guid? CompetitionChallengeId,
    SubmissionKind Kind,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? ProcessedWorkerId,
    string? EvaluatorVersion,
    bool IsCurrentEventDeleted,
    long ProcessingVersion);

public interface IAdminSubmissionStatusReader
{
    Task<AdminSubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken);
}
