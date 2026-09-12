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

    public bool HumanVerificationEnabled { get; set; } = true;

    public bool HumanVerificationRuntimeEnabled { get; set; } = true;

    public HumanVerificationProvider? HumanVerificationProvider { get; set; }

    [MaxLength(2048)]
    public string HumanVerificationCapServerUrl { get; set; } = string.Empty;

    [MaxLength(256)]
    public string HumanVerificationCapSiteKey { get; set; } = string.Empty;

    [MaxLength(4096)]
    public byte[]? HumanVerificationCapSecretCiphertext { get; set; }

    [MaxLength(256)]
    public string HumanVerificationTurnstileSiteKey { get; set; } = string.Empty;

    [MaxLength(4096)]
    public byte[]? HumanVerificationTurnstileSecretCiphertext { get; set; }

    public string[] HumanVerificationTurnstileAllowedHostnames { get; set; } = [];

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

    public bool PublicGatewayEnabled { get; set; }
    [MaxLength(128)]
    public string PublicGatewayConnectorId { get; set; } = string.Empty;
    [MaxLength(2048)]
    public string PublicGatewayOrigin { get; set; } = string.Empty;
    public string[] PublicGatewayDirectOrigins { get; set; } = [];
    [MaxLength(253)]
    public string PublicGatewayRuntimeHost { get; set; } = string.Empty;
    [MaxLength(253)]
    public string? PublicGatewayDirectHostOverride { get; set; }
    public int PublicGatewayMaxPorts { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
