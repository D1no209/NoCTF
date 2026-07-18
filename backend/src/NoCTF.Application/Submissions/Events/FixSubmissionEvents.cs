namespace NoCTF.Application.Submissions.Events;

public sealed record FixArchiveReference(
    string ObjectKey,
    string FileName,
    string ContentType,
    long Length,
    string Sha256);

public sealed record FixSubmissionReceived(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    Guid UploadId,
    FixArchiveReference Archive,
    string IpAddress,
    DateTimeOffset ReceivedAt) : ISubmissionStreamEvent;

public sealed record FixSubmissionEvaluated(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    SubmissionOutcome Outcome,
    bool ConsumedAttempt,
    DateTimeOffset EvaluatedAt,
    string? ErrorCode = null,
    int? Round = null) : ISubmissionStreamEvent;
