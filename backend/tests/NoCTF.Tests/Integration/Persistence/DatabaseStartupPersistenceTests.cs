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
            services.AddDbContext<NoCtfDbContext>(options => options
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention());
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
                var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
                await Assert.That(appliedMigrations).Count().IsEqualTo(7);
                await Assert.That(appliedMigrations[0]).EndsWith("_InitialBaseline");
                await Assert.That(appliedMigrations[1]).EndsWith("_CompetitionProgression");
                await Assert.That(appliedMigrations[2]).EndsWith("_PersistedSsoFlows");
                await Assert.That(appliedMigrations[3]).EndsWith("_PersistedRequestAdmission");
                await Assert.That(appliedMigrations[4]).EndsWith("_CompetitionWebhookOutbox");
                await Assert.That(appliedMigrations[5]).EndsWith("_ProgressionReadableMap");
                await Assert.That(appliedMigrations[6]).EndsWith("_ProgressionNodePrerequisitePolicy");
                var tables = db.Model.GetEntityTypes()
                    .Select(entity => entity.GetTableName())
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .Distinct().ToArray();
                await Assert.That(tables).Contains("competition_collaborators");
                await Assert.That(tables).Contains("team_members");
                await Assert.That(tables).Contains("team_captains");
                await Assert.That(tables).Contains("challenge_definitions");
                await Assert.That(tables).Contains("competition_mode_configurations");
                await Assert.That(tables).Contains("competition_challenge_rules");
                await Assert.That(tables).Contains("runtime_capacity_allocations");
                await Assert.That(tables).Contains("runtime_access_endpoints");
                await Assert.That(tables).Contains("command_receipts");
                await Assert.That(tables).Contains("external_identities");
                await Assert.That(tables).Contains("sso_flows");
                await Assert.That(tables).Contains("request_admission_windows");
                await Assert.That(tables).Contains("request_admission_leases");
                await Assert.That(tables).Contains("competition_webhook_outbox_events");
                await Assert.That(tables).Contains("competition_webhook_deliveries");
                await Assert.That(tables).Contains("competition_webhook_frozen_projections");
                await Assert.That(tables.Any(table =>
                    table.Contains("wolverine", StringComparison.OrdinalIgnoreCase))).IsFalse();
                await Assert.That(db.Model.FindEntityType(typeof(NoCTF.Domain.Runtime.RuntimeInstance))!
                    .GetProperties().Any(property => property.Name == "Urls")).IsFalse();
                await Assert.That(await db.SsoFlows.AnyAsync(ct)).IsFalse();
            }
            await DatabaseStartup.InitializeAsync(provider, configuration, ct);
            await using var verification = provider.CreateAsyncScope();
            var existing = await verification.ServiceProvider.GetRequiredService<NoCtfDbContext>().Users.SingleAsync(ct);
            await Assert.That(existing.Id).IsEqualTo(originalId);
            await Assert.That(existing.PasswordHash).IsEqualTo(originalHash);
        });
    }
}
