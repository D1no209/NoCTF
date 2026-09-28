using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Platform;

public sealed class PlatformSettings : NoCTF.Domain.Shared.IConcurrencyTracked
{
    [Key]
    public short Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid? LogoFileId { get; set; }
    public NoCTF.Domain.Storage.StoredFile? LogoFile { get; set; }

    public bool HumanVerificationEnabled { get; set; } = true;

    public bool HumanVerificationRuntimeEnabled { get; set; } = true;

    public bool HumanVerificationEvaluationEnabled { get; set; } = true;

    public bool CtfPatchVerificationEnabled { get; set; }

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

    public List<HumanVerificationTurnstileHostname> HumanVerificationTurnstileHostnames { get; set; } = [];
    [NotMapped]
    public string[] HumanVerificationTurnstileAllowedHostnames
    {
        get => HumanVerificationTurnstileHostnames.OrderBy(hostname => hostname.Position)
            .Select(hostname => hostname.Hostname).ToArray();
        set => HumanVerificationTurnstileHostnames = (value ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((hostname, position) => new HumanVerificationTurnstileHostname
            {
                PlatformSettingsId = Id,
                Position = position,
                Hostname = hostname
            }).ToList();
    }

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

    public bool SsoEnabled { get; set; }

    [MaxLength(2048)]
    public string SsoPublicBaseUrl { get; set; } = string.Empty;

    public List<SsoProviderConfiguration> SsoProviders { get; set; } = [];

    [NotMapped]
    public SsoConfiguration SsoConfiguration
    {
        get => new()
        {
            Enabled = SsoEnabled,
            PublicBaseUrl = SsoPublicBaseUrl,
            Providers = SsoProviders
        };
        set
        {
            SsoEnabled = value?.Enabled ?? false;
            SsoPublicBaseUrl = value?.PublicBaseUrl ?? string.Empty;
            SsoProviders = value?.Providers ?? [];
        }
    }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HumanVerificationTurnstileHostname
{
    public short PlatformSettingsId { get; set; }
    public int Position { get; set; }
    [MaxLength(253)]
    public string Hostname { get; set; } = string.Empty;
}
