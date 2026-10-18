using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class PlatformPublicAddressPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Upgrade_preserves_public_address_and_all_readers_follow_basic_configuration(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), setup => setup.MigrationsAssembly("NoCTF.Persistence.PostgreSql"))
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, name => name.EndsWith("_ConsolidatePlatformPublicAddress", StringComparison.Ordinal));
            await Assert.That(index).IsGreaterThan(0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1], ct);
            await db.Database.ExecuteSqlRawAsync("UPDATE platform_settings SET email_public_base_url = 'https://original.example', sso_public_base_url = 'https://old-sso.example'", ct);
            await db.Database.MigrateAsync(ct);

            var protector = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions
            {
                EncryptionKey = Convert.ToBase64String(new byte[32])
            }));
            var email = new EmailVerificationConfigurationStore(db, protector);
            var sso = new SsoConfigurationStore(db, protector, new(false, false, new HashSet<string>(), new HashSet<string>()));
            var platform = new PlatformConfigurationStore(db);
            await Assert.That((await platform.GetAsync(ct)).PublicBaseUrl).IsEqualTo("https://original.example");
            await Assert.That((await email.GetAsync(ct)).PublicBaseUrl).IsEqualTo("https://original.example");
            await Assert.That((await sso.GetAsync(ct)).PublicBaseUrl).IsEqualTo("https://original.example");

            await platform.UpdateAsync("NoCTF", null, DateTimeOffset.UtcNow, ct, "https://current.example");
            await Assert.That((await email.GetAsync(ct)).PublicBaseUrl).IsEqualTo("https://current.example");
            await Assert.That((await sso.GetAsync(ct)).PublicBaseUrl).IsEqualTo("https://current.example");
            await Assert.That((await PlatformPublicAddress.ReadAsync(db, ct)).AbsoluteUri).IsEqualTo("https://current.example/");
            var settings = await db.PlatformSettings.SingleAsync(ct);
            var snapshot = settings.SsoConfiguration;
            snapshot.PublicBaseUrl = "https://stale.example";
            settings.SsoConfiguration = snapshot;
            await db.SaveChangesAsync(ct);
            await Assert.That((await platform.GetAsync(ct)).PublicBaseUrl).IsEqualTo("https://current.example");
        });
    }
}
