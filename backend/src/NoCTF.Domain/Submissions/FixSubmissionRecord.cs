namespace NoCTF.Domain.Submissions;

public enum FixVerificationStatus
{
    Created = 0,
    Claimed = 1,
    Verifying = 2,
    Valid = 3,
    TeamFailure = 4,
    PlatformFailed = 5,
    Expired = 6,
    CleanupPending = 7,
    Cleaned = 8,
    AuthorizationPending = 9
}

public sealed class FixSubmissionRecord
{
    public Guid UploadId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Submission? Submission { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? ObjectMetadata { get; set; }
    public FixVerificationStatus VerificationStatus { get; set; }
    public ScoringFailureCode? FailureCategory { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifierVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
}
