using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Persistence.Configurations.Platform;

internal sealed class PlatformSettingsConfiguration
    : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings");
        builder.Property(x=>x.LiveSoloVideoMaximumWidth).HasDefaultValue(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultWidth);
        builder.Property(x=>x.LiveSoloVideoMaximumHeight).HasDefaultValue(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultHeight);
        builder.Property(x=>x.LiveSoloVideoMaximumFramesPerSecond).HasDefaultValue(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultFramesPerSecond);
        builder.Property(x=>x.LiveSoloVideoMaximumBitrateBitsPerSecond).HasDefaultValue(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultBitrateBitsPerSecond);
        builder.Property(x=>x.LiveSoloVideoPolicyStamp).HasDefaultValue(Guid.Parse("00000000-0000-0000-0000-000000000005"));

        // Data annotations cannot seed the singleton platform configuration row.
        builder.HasData(new PlatformSettings
        {
            Id = 1,
            ConcurrencyStamp = Guid.Parse("00000000-0000-0000-0000-000000000001"),
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
            SsoEnabled = false,
            SsoPublicBaseUrl = string.Empty,
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
        builder.HasMany(settings => settings.SsoProviders)
            .WithOne()
            .HasForeignKey(provider => provider.PlatformSettingsId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(settings => settings.SsoProviders).AutoInclude();
        builder.HasMany(settings => settings.HumanVerificationTurnstileHostnames)
            .WithOne()
            .HasForeignKey(hostname => hostname.PlatformSettingsId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(settings => settings.HumanVerificationTurnstileHostnames).AutoInclude();
        builder.HasOne(settings => settings.LogoFile).WithMany()
            .HasForeignKey(settings => settings.LogoFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SsoProviderConfigurationEntityConfiguration
    : IEntityTypeConfiguration<SsoProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<SsoProviderConfiguration> builder)
    {
        builder.ToTable("sso_providers");
        builder.HasKey(provider => provider.Id);
        builder.Property(provider => provider.Id).ValueGeneratedNever();
        builder.HasDiscriminator(provider => provider.Protocol)
            .HasValue<OidcSsoProviderConfiguration>(NoCTF.Domain.Identity.SsoProtocol.Oidc)
            .HasValue<CasSsoProviderConfiguration>(NoCTF.Domain.Identity.SsoProtocol.Cas);
        builder.Property(provider => provider.Name).HasMaxLength(160).IsRequired();
        builder.Property(provider => provider.IconUrl).HasMaxLength(2048);
        builder.HasMany(provider => provider.AllowedHostEntries)
            .WithOne()
            .HasForeignKey(host => host.SsoProviderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(provider => provider.AllowedHostEntries).AutoInclude();
    }
}

internal sealed class OidcSsoProviderConfigurationEntityConfiguration
    : IEntityTypeConfiguration<OidcSsoProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<OidcSsoProviderConfiguration> builder)
    {
        builder.Property(value => value.MfaTrustEnabled).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(value => value.MfaAuthenticationMaxAgeSeconds).HasDefaultValue(300).ValueGeneratedNever();
        builder.Property(value => value.MfaTrustPolicyId).HasDefaultValue(Guid.Empty).ValueGeneratedNever();
        builder.Property(value => value.Issuer).HasMaxLength(2048);
        builder.Property(value => value.DiscoveryUrl).HasMaxLength(2048);
        builder.Property(value => value.ClientId).HasMaxLength(512);
        builder.HasMany(value => value.ScopeEntries)
            .WithOne()
            .HasForeignKey(scope => scope.SsoProviderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(value => value.ScopeEntries).AutoInclude();
        builder.HasMany(value => value.MfaAcrEntries).WithOne().HasForeignKey(value => value.SsoProviderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(value => value.MfaAcrEntries).AutoInclude();
        builder.HasMany(value => value.MfaAmrGroups).WithOne().HasForeignKey(value => value.SsoProviderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(value => value.MfaAmrGroups).AutoInclude();
    }
}

internal sealed class OidcMfaAmrGroupConfiguration : IEntityTypeConfiguration<OidcMfaAmrGroup>
{
    public void Configure(EntityTypeBuilder<OidcMfaAmrGroup> builder)
    {
        builder.HasIndex(value => new { value.SsoProviderId, value.Position }).IsUnique();
        builder.HasMany(value => value.Values).WithOne().HasForeignKey(value => value.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(value => value.Values).AutoInclude();
    }
}

internal sealed class OidcMfaAcrConfiguration : IEntityTypeConfiguration<OidcMfaAcr>
{
    public void Configure(EntityTypeBuilder<OidcMfaAcr> builder) => builder.HasKey(value => new { value.SsoProviderId, value.Position });
}

internal sealed class OidcMfaAmrValueConfiguration : IEntityTypeConfiguration<OidcMfaAmrValue>
{
    public void Configure(EntityTypeBuilder<OidcMfaAmrValue> builder) => builder.HasKey(value => new { value.GroupId, value.Position });
}

internal sealed class CasSsoProviderConfigurationEntityConfiguration
    : IEntityTypeConfiguration<CasSsoProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<CasSsoProviderConfiguration> builder)
    {
        builder.Property(value => value.IdentityNamespace).HasMaxLength(512);
        builder.Property(value => value.LoginUrl).HasMaxLength(2048);
        builder.Property(value => value.ServiceValidateUrl).HasMaxLength(2048);
        builder.Property(value => value.DisplayNameAttribute).HasMaxLength(256);
    }
}

internal sealed class SsoProviderAllowedHostConfiguration
    : IEntityTypeConfiguration<SsoProviderAllowedHost>
{
    public void Configure(EntityTypeBuilder<SsoProviderAllowedHost> builder)
    {
        builder.ToTable("sso_provider_allowed_hosts");
        builder.HasKey(host => new { host.SsoProviderId, host.Position });
        builder.Property(host => host.Position).ValueGeneratedNever();
    }
}

internal sealed class OidcSsoScopeConfiguration : IEntityTypeConfiguration<OidcSsoScope>
{
    public void Configure(EntityTypeBuilder<OidcSsoScope> builder)
    {
        builder.ToTable("sso_provider_oidc_scopes");
        builder.HasKey(scope => new { scope.SsoProviderId, scope.Position });
        builder.Property(scope => scope.Position).ValueGeneratedNever();
    }
}

internal sealed class HumanVerificationTurnstileHostnameConfiguration
    : IEntityTypeConfiguration<HumanVerificationTurnstileHostname>
{
    public void Configure(EntityTypeBuilder<HumanVerificationTurnstileHostname> builder)
    {
        builder.ToTable("human_verification_turnstile_hostnames");
        builder.HasKey(hostname => new { hostname.PlatformSettingsId, hostname.Position });
        builder.Property(hostname => hostname.Position).ValueGeneratedNever();
        builder.HasIndex(hostname => new { hostname.PlatformSettingsId, hostname.Hostname })
            .IsUnique();
    }
}
