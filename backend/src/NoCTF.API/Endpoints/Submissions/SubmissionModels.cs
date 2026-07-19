using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Submissions;

public sealed record AcceptedSubmissionResponse(Guid SubmissionId, DateTimeOffset ReceivedAt);

public enum SubmissionStatusState
{
    Unprocessed,
    Success,
    Failure
}

public sealed record SubmissionStatusResponse(
    Guid SubmissionId,
    SubmissionKind Kind,
    SubmissionStatusState State,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    ScoringFailureCode? FailureCode);
