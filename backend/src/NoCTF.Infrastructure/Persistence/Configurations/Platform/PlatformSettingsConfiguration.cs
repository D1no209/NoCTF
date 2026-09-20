using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Platform;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace NoCTF.Infrastructure.Persistence.Configurations.Platform;

internal sealed class PlatformSettingsConfiguration
    : IEntityTypeConfiguration<PlatformSettings>
{
    private static readonly JsonSerializerOptions SsoJsonOptions =
        new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings");

        // Data annotations cannot seed the singleton platform configuration row.
        builder.HasData(new PlatformSettings
        {
            Id = 1,
            Name = "NoCTF",
            Description = null,
            LogoFileId = null,
            HumanVerificationEnabled = true,
            HumanVerificationRuntimeEnabled = true,
            HumanVerificationEvaluationEnabled = true,
            CtfPatchVerificationEnabled = false,
            HumanVerificationProvider = null,
            HumanVerificationCapServerUrl = string.Empty,
            HumanVerificationCapSiteKey = string.Empty,
            HumanVerificationCapSecretCiphertext = null,
            HumanVerificationTurnstileSiteKey = string.Empty,
            HumanVerificationTurnstileSecretCiphertext = null,
            HumanVerificationTurnstileAllowedHostnames = [],
            EmailVerificationEnabled = false,
            EmailPublicBaseUrl = "http://localhost:5000",
            EmailVerificationTokenLifetimeMinutes = 1440,
            EmailVerificationResendCooldownSeconds = 60,
            EmailPasswordResetTokenLifetimeMinutes = 30,
            EmailPasswordResetCooldownSeconds = 60,
            EmailPasswordResetMaxRequestsPerHour = 5,
            EmailSmtpHost = string.Empty,
            EmailSmtpPort = 587,
            EmailSmtpSecurityMode = null,
            EmailSmtpUserName = string.Empty,
            EmailSmtpPasswordCiphertext = null,
            EmailSmtpFromAddress = string.Empty,
            EmailSmtpFromName = "NoCTF",
            EmailSmtpTimeoutSeconds = 30,
            SsoConfiguration = new SsoConfiguration(),
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
        // Existing deployments required verification for Runtime and Evaluation operations.
        // Keep that behavior when adding administrator-controlled switches.
        builder.Property(settings => settings.HumanVerificationRuntimeEnabled)
            .HasDefaultValue(true)
            .ValueGeneratedNever();
        builder.Property(settings => settings.HumanVerificationEvaluationEnabled)
            .HasDefaultValue(true)
            .ValueGeneratedNever();
        builder.Property(settings => settings.CtfPatchVerificationEnabled)
            .HasDefaultValue(false)
            .ValueGeneratedNever();
        builder.Property(settings => settings.HumanVerificationProvider)
            .HasConversion<short>();
        builder.Property(settings => settings.EmailSmtpSecurityMode).HasConversion<short>();
        builder.Property(settings => settings.SsoConfiguration)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, SsoJsonOptions),
                value => JsonSerializer.Deserialize<SsoConfiguration>(value, SsoJsonOptions)
                    ?? new SsoConfiguration(),
                new ValueComparer<SsoConfiguration>(
                    (left, right) => Serialize(left) == Serialize(right),
                    value => Serialize(value).GetHashCode(StringComparison.Ordinal),
                    value => Deserialize(Serialize(value))));
        builder.HasOne(settings => settings.LogoFile).WithMany()
            .HasForeignKey(settings => settings.LogoFileId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_platform_settings_singleton", "id = 1");
            table.HasCheckConstraint(
                "ck_platform_settings_sso_configuration",
                "jsonb_typeof(\"sso_configuration\") = 'object'"
                + " AND (\"sso_configuration\" ->> 'schemaVersion')::integer = 1"
                + " AND jsonb_typeof(\"sso_configuration\" -> 'providers') = 'array'"
                + " AND jsonb_array_length(\"sso_configuration\" -> 'providers') <= 16");
        });
    }

    private static string Serialize(SsoConfiguration? value) =>
        JsonSerializer.Serialize(value ?? new SsoConfiguration(), SsoJsonOptions);

    private static SsoConfiguration Deserialize(string value) =>
        JsonSerializer.Deserialize<SsoConfiguration>(value, SsoJsonOptions)
        ?? new SsoConfiguration();
}
