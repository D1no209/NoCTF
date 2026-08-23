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

        // Data annotations cannot seed the singleton platform configuration row.
        builder.HasData(new PlatformSettings
        {
            Id = 1,
            Name = "NoCTF",
            Description = null,
            LogoFileId = null,
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
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
        builder.Property(settings => settings.EmailSmtpSecurityMode).HasConversion<short>();
        builder.HasOne(settings => settings.LogoFile).WithMany()
            .HasForeignKey(settings => settings.LogoFileId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("ck_platform_settings_singleton", "id = 1"));
    }
}
