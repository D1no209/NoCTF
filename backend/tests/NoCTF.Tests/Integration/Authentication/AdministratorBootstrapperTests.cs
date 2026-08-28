using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class AdministratorBootstrapperTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Bootstrap_is_idempotent_and_never_promotes_an_existing_user(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_admin_bootstrap")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var configuration = Configuration("Admin888");
            var bootstrapper = new AdministratorBootstrapper(
                db,
                Options.Create(configuration.GetSection(
                    SeedAdministratorOptions.SectionName).Get<SeedAdministratorOptions>()!),
                hasher,
                TimeProvider.System);

            await bootstrapper.SeedAsync(cancellationToken);
            var administrator = await db.Users.SingleAsync(cancellationToken);
            await Assert.That(administrator.Role).IsEqualTo(UserRole.Administrator);
            await Assert.That(administrator.EmailVerifiedAt).IsNotNull();
            await Assert.That(hasher.VerifyHashedPassword(
                    administrator,
                    administrator.PasswordHash,
                    "Admin888"))
                .IsNotEqualTo(PasswordVerificationResult.Failed);

            var originalHash = administrator.PasswordHash;
            var changedConfiguration = Configuration("Change88");
            await new AdministratorBootstrapper(
                    db,
                    Options.Create(changedConfiguration.GetSection(
                        SeedAdministratorOptions.SectionName).Get<SeedAdministratorOptions>()!),
                    hasher,
                    TimeProvider.System)
                .SeedAsync(cancellationToken);
            await db.Entry(administrator).ReloadAsync(cancellationToken);
            await Assert.That(administrator.PasswordHash).IsEqualTo(originalHash);

            db.Users.Remove(administrator);
            await db.SaveChangesAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                UserName = "ctf-e2e-admin",
                NormalizedUserName = "CTF-E2E-ADMIN",
                Email = "player@example.test",
                PasswordHash = "test",
                Role = UserRole.User,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);

            await Assert.That(async () => await bootstrapper.SeedAsync(cancellationToken))
                .Throws<InvalidOperationException>();
            await db.Entry(db.Users.Local.Single()).ReloadAsync(cancellationToken);
            await Assert.That(db.Users.Local.Single().Role).IsEqualTo(UserRole.User);
        });
    }

    private static IConfiguration Configuration(string password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedAdmin:UserName"] = "ctf-e2e-admin",
                ["SeedAdmin:Email"] = "admin@ctf-e2e.test",
                ["SeedAdmin:Password"] = password
            })
            .Build();
}
