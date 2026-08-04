using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Platform;

public sealed class PlatformSettings
{
    [Key]
    public short Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(1024)]
    public string? LogoObjectKey { get; set; }

    [ConcurrencyCheck]
    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
