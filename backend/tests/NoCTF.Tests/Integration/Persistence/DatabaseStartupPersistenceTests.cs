using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class DatabaseStartupPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Startup_migrates_empty_database_and_preserves_existing_administrator(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_startup").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            services.AddSingleton<IOptions<SeedAdministratorOptions>>(Options.Create(new SeedAdministratorOptions
            {
                UserName = "startup-admin",
                Email = "startup-admin@example.test",
                Password = "Startup-Test-Password1!"
            }));
            services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddScoped<AdministratorBootstrapper>();
            await using var provider = services.BuildServiceProvider();
            var configuration = new ConfigurationBuilder().Build();
            await DatabaseStartup.InitializeAsync(provider, configuration, ct);
            Guid originalId;
            string originalHash;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var user = await db.Users.SingleAsync(ct);
                originalId = user.Id;
                originalHash = user.PasswordHash;
                await Assert.That(user.Role).IsEqualTo(UserRole.Administrator);
                await Assert.That(await db.Database.GetPendingMigrationsAsync(ct)).IsEmpty();
            }
            await DatabaseStartup.InitializeAsync(provider, configuration, ct);
            await using var verification = provider.CreateAsyncScope();
            var existing = await verification.ServiceProvider.GetRequiredService<NoCtfDbContext>().Users.SingleAsync(ct);
            await Assert.That(existing.Id).IsEqualTo(originalId);
            await Assert.That(existing.PasswordHash).IsEqualTo(originalHash);
        });
    }
}
