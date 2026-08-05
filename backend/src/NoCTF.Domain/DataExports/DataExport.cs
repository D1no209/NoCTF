using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.DataExports;

public enum DataExportScope : short
{
    CompetitionArchive,
    PlatformAudit
}

public enum DataExportStatus : short
{
    Queued,
    Processing,
    Available,
    Failed,
    Expired
}

public enum DataExportFailureCode : short
{
    SubjectNotFound,
    SizeLimitExceeded,
    GenerationFailed,
    ObjectStorageFailed
}

public sealed class DataExport
{
    public Guid Id { get; set; }
    public DataExportScope Scope { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public bool IncludeProtectedFlags { get; set; }
    [MaxLength(512)]
    public string? Reason { get; set; }
    public DataExportStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset PurgeAt { get; set; }
    [MaxLength(1024)]
    public string? ObjectKey { get; set; }
    [MaxLength(256)]
    public string? FileName { get; set; }
    [MaxLength(128)]
    public string? ContentType { get; set; }
    public long? Length { get; set; }
    [MaxLength(64)]
    public string? Sha256 { get; set; }
    public DataExportFailureCode? FailureCode { get; set; }
    [MaxLength(512)]
    public string? FailureDetail { get; set; }
}
