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
    string IdempotencyKey,
    FixArchiveReference Archive,
    string IpAddress,
    DateTimeOffset ReceivedAt)
{
    public override string ToString() =>
        $"{nameof(FixSubmissionReceived)} {{ SubmissionId = {SubmissionId}, UploadId = {UploadId}, Archive = [REDACTED] }}";
}
