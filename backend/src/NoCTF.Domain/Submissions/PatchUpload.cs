using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Submissions;

public sealed class PatchUpload
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public Guid UploadedByUserId { get; set; }
    [MaxLength(1024)]
    public string ObjectKey { get; set; } = string.Empty;
    [MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;
    [MaxLength(255)]
    public string ContentType { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    [Length(32, 32)]
    public byte[] Sha256 { get; set; } = [];
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? SubmissionId { get; set; }
}
