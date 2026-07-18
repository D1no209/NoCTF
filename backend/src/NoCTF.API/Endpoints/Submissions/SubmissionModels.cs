using NoCTF.Application.Submissions.Events;

namespace NoCTF.API.Endpoints.Submissions;

public sealed record AcceptedSubmissionResponse(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed record SubmissionStatusResponse(
    Guid SubmissionId,
    SubmissionKind Kind,
    SubmissionOutcome Outcome,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode);
