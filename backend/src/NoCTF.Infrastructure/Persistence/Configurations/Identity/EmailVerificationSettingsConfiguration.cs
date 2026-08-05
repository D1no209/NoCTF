using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class EmailVerificationSettingsConfiguration
    : IEntityTypeConfiguration<EmailVerificationSettings>
{
    public void Configure(EntityTypeBuilder<EmailVerificationSettings> builder)
    {
        builder.ToTable("email_verification_settings");

        // Data annotations cannot seed the singleton platform configuration row.
        builder.HasData(new EmailVerificationSettings
        {
            Id = 1,
            Enabled = false,
            PublicBaseUrl = "https://noctf.local",
            TokenLifetimeMinutes = 1440,
            ResendCooldownSeconds = 60,
            PasswordResetTokenLifetimeMinutes = 30,
            PasswordResetCooldownSeconds = 60,
            PasswordResetMaxRequestsPerHour = 3,
            SmtpHost = string.Empty,
            SmtpPort = 587,
            SmtpEnableSsl = true,
            SmtpUserName = string.Empty,
            SmtpPasswordCiphertext = null,
            SmtpFromAddress = string.Empty,
            SmtpFromName = "NoCTF",
            SmtpTimeoutSeconds = 10,
            Revision = 1,
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
    }
}
