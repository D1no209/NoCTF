using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class EmailVerificationConfigurationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Smtp_password_is_encrypted_write_only_and_revision_fenced(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
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

            var passwordUpdated = await store.ReplacePasswordAsync(
                initial.Revision,
                "smtp-secret",
                DateTimeOffset.UtcNow,
                cancellationToken);
            await Assert.That(passwordUpdated).IsNotNull();
            await Assert.That(passwordUpdated!.SmtpPasswordConfigured).IsTrue();
            await Assert.That(typeof(EmailVerificationConfigurationView).GetProperty("SmtpPassword"))
                .IsNull();

            var updated = await store.UpdateAsync(new(
                Enabled: true,
                PublicBaseUrl: "https://noctf.example",
                TokenLifetimeMinutes: 120,
                ResendCooldownSeconds: 45,
                SmtpHost: "smtp.example",
                SmtpPort: 587,
                SmtpEnableSsl: true,
                SmtpUserName: "mailer",
                SmtpFromAddress: "no-reply@noctf.example",
                SmtpFromName: "NoCTF",
                SmtpTimeoutSeconds: 10,
                ExpectedRevision: passwordUpdated.Revision,
                Now: DateTimeOffset.UtcNow), cancellationToken);
            await Assert.That(updated).IsNotNull();

            var delivery = await store.GetDeliveryConfigurationAsync(
                requireEnabled: true,
                cancellationToken);
            await Assert.That(delivery).IsNotNull();
            await Assert.That(delivery!.SmtpPassword).IsEqualTo("smtp-secret");

            var staleUpdate = await store.UpdateAsync(new(
                Enabled: false,
                PublicBaseUrl: "https://stale.example",
                TokenLifetimeMinutes: 120,
                ResendCooldownSeconds: 45,
                SmtpHost: "smtp.example",
                SmtpPort: 587,
                SmtpEnableSsl: true,
                SmtpUserName: "mailer",
                SmtpFromAddress: "no-reply@noctf.example",
                SmtpFromName: "NoCTF",
                SmtpTimeoutSeconds: 10,
                ExpectedRevision: passwordUpdated.Revision,
                Now: DateTimeOffset.UtcNow), cancellationToken);
            await Assert.That(staleUpdate).IsNull();
        });
    }
}
