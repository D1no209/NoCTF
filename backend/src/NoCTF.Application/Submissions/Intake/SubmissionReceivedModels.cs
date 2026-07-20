using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Accepted Flag input. Its protected value is redacted from string representations.</summary>
public sealed record FlagSubmissionReceived
{
    public required Guid SubmissionId { get; init; }
    public required Guid CompetitionId { get; init; }
    public required Guid TeamId { get; init; }
    public required Guid ChallengeId { get; init; }
    public required Guid UserId { get; init; }
    public required FlagFingerprint FlagFingerprint { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string IpAddress { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public AwdAttackTarget? AttackTarget { get; init; }
    public Guid? StageId { get; init; }
    public Guid? ChallengeInstanceId { get; init; }

    public override string ToString() =>
        $"{nameof(FlagSubmissionReceived)} {{ SubmissionId = {SubmissionId}, Flag = [REDACTED] }}";
}

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
