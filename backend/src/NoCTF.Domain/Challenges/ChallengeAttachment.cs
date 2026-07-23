namespace NoCTF.Domain.Challenges;

/// <summary>References an object-storage attachment without embedding transport details.</summary>
public sealed class ChallengeAttachment
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
    public byte[] Sha256Bytes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
