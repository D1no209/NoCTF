using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Identity;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class EmailVerificationConfigurationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Smtp_password_is_encrypted_write_only_and_last_write_wins(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_email_verification")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmailVerification:EncryptionKey"] = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))
                })
                .Build();

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            var store = new EmailVerificationConfigurationStore(
                db,
                new EmailVerificationSecretProtector(configuration));
            var initial = await store.GetAsync(cancellationToken);
            await Assert.That(initial.SmtpSecurityMode)
                .IsEqualTo(SmtpSecurityMode.None);
            await Assert.That(await db.PlatformSettings.CountAsync(cancellationToken))
                .IsEqualTo(1);

            var passwordUpdated = await store.ReplacePasswordAsync(
                "smtp-secret",
                DateTimeOffset.UtcNow,
                cancellationToken);
            await Assert.That(passwordUpdated).IsNotNull();
            await Assert.That(passwordUpdated!.SmtpPasswordConfigured).IsTrue();
            await Assert.That(typeof(EmailVerificationConfigurationView).GetProperty("SmtpPassword"))
                .IsNull();
            var persistedSettings = await db.PlatformSettings.AsNoTracking()
                .SingleAsync(cancellationToken);
            await Assert.That(persistedSettings.EmailSmtpPasswordCiphertext)
                .IsNotNull();
            await Assert.That(persistedSettings.EmailSmtpPasswordCiphertext!)
                .IsNotEquivalentTo(Encoding.UTF8.GetBytes("smtp-secret"));

            var updated = await store.UpdateAsync(new(
                Enabled: true,
                PublicBaseUrl: "https://noctf.example",
                TokenLifetimeMinutes: 120,
                ResendCooldownSeconds: 45,
                PasswordResetTokenLifetimeMinutes: 30,
                PasswordResetCooldownSeconds: 60,
                PasswordResetMaxRequestsPerHour: 3,
                SmtpHost: "smtp.example",
                SmtpPort: 587,
                SmtpSecurityMode: SmtpSecurityMode.SslOnConnect,
                SmtpUserName: "mailer",
                SmtpFromAddress: "no-reply@noctf.example",
                SmtpFromName: "NoCTF",
                SmtpTimeoutSeconds: 10,
                Now: DateTimeOffset.UtcNow), cancellationToken);
            await Assert.That(updated).IsNotNull();

            var delivery = await store.GetDeliveryConfigurationAsync(
                requireEnabled: true,
                cancellationToken);
            await Assert.That(delivery).IsNotNull();
            await Assert.That(delivery!.SmtpPassword).IsEqualTo("smtp-secret");
            await Assert.That(delivery.SmtpSecurityMode)
                .IsEqualTo(SmtpSecurityMode.SslOnConnect);

            var staleUpdate = await store.UpdateAsync(new(
                Enabled: false,
                PublicBaseUrl: "https://stale.example",
                TokenLifetimeMinutes: 120,
                ResendCooldownSeconds: 45,
                PasswordResetTokenLifetimeMinutes: 30,
                PasswordResetCooldownSeconds: 60,
                PasswordResetMaxRequestsPerHour: 3,
                SmtpHost: "smtp.example",
                SmtpPort: 587,
                SmtpSecurityMode: SmtpSecurityMode.StartTls,
                SmtpUserName: "mailer",
                SmtpFromAddress: "no-reply@noctf.example",
                SmtpFromName: "NoCTF",
                SmtpTimeoutSeconds: 10,
                Now: DateTimeOffset.UtcNow), cancellationToken);
            await Assert.That(staleUpdate).IsNull();
        });
    }
}
