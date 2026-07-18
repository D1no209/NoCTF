namespace NoCTF.Domain.Storage;

/// <summary>Tracks an authorized, single-use Fix archive upload.</summary>
public sealed class FixUploadSession
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public required string ObjectKey { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long ExpectedLength { get; set; }
    public required string ExpectedSha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
    public Guid? ConsumedBySubmissionId { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
