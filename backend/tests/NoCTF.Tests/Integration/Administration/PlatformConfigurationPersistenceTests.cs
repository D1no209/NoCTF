using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class PlatformConfigurationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Singleton_configuration_persists_with_last_write_wins(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
            var logoFileId = Guid.CreateVersion7(now);
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels)
                .Services
                .BuildServiceProvider();
            var caches = cacheServices.GetRequiredService<IFusionCacheProvider>();

            await using (var db = new NoCtfDbContext(options))
                await db.Database.MigrateAsync(cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new PlatformConfigurationStore(db, caches);
                var seeded = await store.GetAsync(cancellationToken);
                var updated = await store.UpdateAsync(
                    "NoCTF Arena",
                    "Production competition platform",
                    now,
                    cancellationToken);

                await Assert.That(seeded.Name).IsEqualTo("NoCTF");
                await Assert.That(updated).IsNotNull();

                db.Files.Add(new StoredFile
                {
                    Id = logoFileId,
                    ObjectKey = "platform/logo/test.png",
                    FileName = "test.png",
                    ContentType = "image/png",
                    ByteLength = 8,
                    Sha256 = new byte[32],
                    CreatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken);
                var logo = await store.ReplaceLogoAsync(
                    logoFileId,
                    now.AddSeconds(1),
                    cancellationToken);
                await Assert.That(logo).IsNotNull();
                await Assert.That(logo!.Configuration.LogoFileId).IsEqualTo(logoFileId);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new PlatformConfigurationStore(db, caches);
                var latest = await store.UpdateAsync(
                    "Latest name",
                    null,
                    now.AddMinutes(1),
                    cancellationToken);
                var persisted = await store.GetAsync(cancellationToken);

                await Assert.That(latest.Name).IsEqualTo("Latest name");
                await Assert.That(persisted.Name).IsEqualTo("Latest name");
                await Assert.That(persisted.Description).IsNull();
                await Assert.That(persisted.LogoFileId).IsEqualTo(logoFileId);
            }
        });
    }
}
