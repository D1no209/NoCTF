namespace NoCTF.Domain.Challenges;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>References an object-storage attachment without embedding transport details.</summary>
public sealed class ChallengeAttachment
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid FileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile File { get; set; } = null!;
    [NotMapped] public string ObjectKey => File.ObjectKey;
    [NotMapped] public string FileName => File.FileName;
    [NotMapped] public string ContentType => File.ContentType;
    [NotMapped] public long Length => File.ByteLength;
    [NotMapped] public byte[] Sha256Bytes => File.Sha256;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
