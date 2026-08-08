using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Storage;

/// <summary>Immutable metadata for one object stored outside PostgreSQL.</summary>
public sealed class StoredFile
{
    public Guid Id { get; set; }

    [MaxLength(1024)]
    public string ObjectKey { get; set; } = string.Empty;

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long ByteLength { get; set; }

    [Length(32, 32)]
    public byte[] Sha256 { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
