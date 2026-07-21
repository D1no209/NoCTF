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
    public string Sha256 { get; set; } = string.Empty;
}
