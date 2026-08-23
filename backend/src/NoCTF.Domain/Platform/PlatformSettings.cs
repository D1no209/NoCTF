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

    public Guid? LogoFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? LogoFile { get; set; }

    public bool EmailVerificationEnabled { get; set; }

    [MaxLength(2048)]
    public string EmailPublicBaseUrl { get; set; } = string.Empty;

    public int EmailVerificationTokenLifetimeMinutes { get; set; }
    public int EmailVerificationResendCooldownSeconds { get; set; }
    public int EmailPasswordResetTokenLifetimeMinutes { get; set; }
    public int EmailPasswordResetCooldownSeconds { get; set; }
    public int EmailPasswordResetMaxRequestsPerHour { get; set; }

    [MaxLength(253)]
    public string EmailSmtpHost { get; set; } = string.Empty;

    public int EmailSmtpPort { get; set; }
    public NoCTF.Domain.Identity.SmtpSecurityMode? EmailSmtpSecurityMode { get; set; }

    [MaxLength(320)]
    public string EmailSmtpUserName { get; set; } = string.Empty;

    [MaxLength(2048)]
    public byte[]? EmailSmtpPasswordCiphertext { get; set; }

    [MaxLength(320)]
    public string EmailSmtpFromAddress { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EmailSmtpFromName { get; set; } = string.Empty;

    public int EmailSmtpTimeoutSeconds { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
