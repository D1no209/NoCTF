using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests;

internal static class SmtpDeliveryTestData
{
    public static (IDbContextFactory<NoCtfDbContext> Factory, PlatformSecretProtector Secrets)
        Create(
            EmailVerificationDeliveryConfiguration configuration,
            Guid userId,
            bool includeUser = true)
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase($"smtp-delivery-{Guid.NewGuid():N}")
            .Options;
        var secrets = new PlatformSecretProtector(Options.Create(
            new EmailVerificationProtectionOptions
            {
                EncryptionKey = Convert.ToBase64String(new byte[32])
            }));
        using var db = new NoCtfDbContext(options);
        db.PlatformSettings.Add(new PlatformSettings
        {
            Id = 1,
            Name = "NoCTF",
            EmailVerificationEnabled = configuration.Enabled,
            EmailPublicBaseUrl = configuration.PublicBaseUrl,
            EmailVerificationTokenLifetimeMinutes = 30,
            EmailVerificationResendCooldownSeconds = 60,
            EmailPasswordResetTokenLifetimeMinutes = 30,
            EmailPasswordResetCooldownSeconds = 60,
            EmailPasswordResetMaxRequestsPerHour = 5,
            EmailSmtpHost = configuration.SmtpHost,
            EmailSmtpPort = configuration.SmtpPort,
            EmailSmtpSecurityMode = configuration.SmtpSecurityMode,
            EmailSmtpUserName = configuration.SmtpUserName,
            EmailSmtpPasswordCiphertext = configuration.SmtpPassword is null
                ? null
                : secrets.Protect(
                    configuration.SmtpPassword,
                    PlatformSecretPurpose.EmailSmtpPassword),
            EmailSmtpFromAddress = configuration.SmtpFromAddress,
            EmailSmtpFromName = configuration.SmtpFromName,
            EmailSmtpTimeoutSeconds = configuration.SmtpTimeoutSeconds,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        if (includeUser)
        {
            db.Users.Add(new User
            {
                Id = userId,
                UserName = "你好-admin",
                NormalizedUserName = "你好-ADMIN",
                Email = "admin@noctf.test",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        db.SaveChanges();
        return (new Factory(options), secrets);
    }

    private sealed class Factory(DbContextOptions<NoCtfDbContext> options)
        : IDbContextFactory<NoCtfDbContext>
    {
        public NoCtfDbContext CreateDbContext() => new(options);
    }
}
