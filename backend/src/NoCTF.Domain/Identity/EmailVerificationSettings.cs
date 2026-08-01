using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Identity;

public sealed class EmailVerificationSettings
{
    [Key]
    public short Id { get; set; }

    public bool Enabled { get; set; }

    [MaxLength(2048)]
    public string PublicBaseUrl { get; set; } = string.Empty;

    public int TokenLifetimeMinutes { get; set; }
    public int ResendCooldownSeconds { get; set; }

    [MaxLength(253)]
    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; }
    public bool SmtpEnableSsl { get; set; }

    [MaxLength(320)]
    public string SmtpUserName { get; set; } = string.Empty;

    [MaxLength(2048)]
    public byte[]? SmtpPasswordCiphertext { get; set; }

    [MaxLength(320)]
    public string SmtpFromAddress { get; set; } = string.Empty;

    [MaxLength(100)]
    public string SmtpFromName { get; set; } = string.Empty;

    public int SmtpTimeoutSeconds { get; set; }

    [ConcurrencyCheck]
    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
