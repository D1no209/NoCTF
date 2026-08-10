using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Gameplay;

public sealed class PatchUpload
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid FileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile File { get; set; } = null!;
    [NotMapped] public string ObjectKey => File.ObjectKey;
    [NotMapped] public string OriginalFileName => File.FileName;
    [NotMapped] public string ContentType => File.ContentType;
    [NotMapped] public long ByteLength => File.ByteLength;
    [NotMapped] public byte[] Sha256 => File.Sha256;
    public DateTimeOffset UploadedAt { get; set; }
}
