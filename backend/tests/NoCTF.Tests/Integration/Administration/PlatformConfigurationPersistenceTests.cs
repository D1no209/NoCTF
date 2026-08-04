using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class PlatformConfigurationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Singleton_configuration_persists_and_rejects_a_stale_revision(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_platform_configuration")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;

            await using (var db = new NoCtfDbContext(options))
                await db.Database.MigrateAsync(cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new PlatformConfigurationStore(db);
                var seeded = await store.GetAsync(cancellationToken);
                var updated = await store.UpdateAsync(
                    "NoCTF Arena",
                    "Production competition platform",
                    seeded.Revision,
                    now,
                    cancellationToken);

                await Assert.That(seeded.Name).IsEqualTo("NoCTF");
                await Assert.That(updated).IsNotNull();
                await Assert.That(updated!.Revision).IsEqualTo(seeded.Revision + 1);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new PlatformConfigurationStore(db);
                var stale = await store.UpdateAsync(
                    "Stale name",
                    null,
                    1,
                    now.AddMinutes(1),
                    cancellationToken);
                var persisted = await store.GetAsync(cancellationToken);

                await Assert.That(stale).IsNull();
                await Assert.That(persisted.Name).IsEqualTo("NoCTF Arena");
                await Assert.That(persisted.Description)
                    .IsEqualTo("Production competition platform");
            }
        });
    }
}
