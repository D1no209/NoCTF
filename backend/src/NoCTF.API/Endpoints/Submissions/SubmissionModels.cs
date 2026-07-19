using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Submissions;

public sealed record AcceptedSubmissionResponse(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed record SubmissionStatusResponse(
    Guid SubmissionId,
    SubmissionKind Kind,
    string State,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    ScoringFailureCode? FailureCode);
