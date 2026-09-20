using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class SsoPersistenceTests
{
    private const string Image =
        "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193";

    [Test]
    [Timeout(300_000)]
    public async Task Migration_enforces_complete_and_unique_external_identity_bindings(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_sso_binding", cancellationToken);
            var options = OptionsFor(postgres.ConnectionString);
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(settings.SsoConfiguration.Enabled).IsFalse();
            await Assert.That(settings.SsoConfiguration.SchemaVersion).IsEqualTo(1);

            var now = DateTimeOffset.UtcNow;
            var providerId = Guid.CreateVersion7(now);
            var administrator = UserWithoutIdentity(
                Guid.CreateVersion7(now.AddMilliseconds(1)),
                "sso-admin",
                now);
            administrator.Role = UserRole.Administrator;
            var anonymizedUser = UserWithIdentity(
                Guid.CreateVersion7(now.AddMilliseconds(2)),
                "sso-anonymized",
                providerId,
                "subject-anonymized",
                now);
            db.Users.AddRange(administrator, anonymizedUser);
            await db.SaveChangesAsync(cancellationToken);
            var deletion = await new UserAccountAdministrationStore(db).DeleteAsync(
                anonymizedUser.Id,
                administrator.Id,
                NoCTF.Application.Administration.UserAccounts.UserDeletionMode.Anonymize,
                "SSO anonymization regression",
                now.AddMinutes(1),
                cancellationToken);
            await Assert.That(deletion.State).IsEqualTo(
                NoCTF.Application.Administration.UserAccounts.UserDeletionState.Anonymized);
            var cleared = await db.Users.AsNoTracking().SingleAsync(
                user => user.Id == anonymizedUser.Id,
                cancellationToken);
            await Assert.That(cleared.ExternalIdentityProviderId).IsNull();
            await Assert.That(cleared.ExternalIdentitySubject).IsNull();

            db.Users.Add(UserWithIdentity(
                Guid.CreateVersion7(now.AddSeconds(1)),
                "sso-one",
                providerId,
                "subject-1",
                now));
            await db.SaveChangesAsync(cancellationToken);

            db.Users.Add(UserWithIdentity(
                Guid.CreateVersion7(now.AddSeconds(2)),
                "sso-two",
                providerId,
                "subject-1",
                now));
            await Assert.That(async () => await db.SaveChangesAsync(cancellationToken))
                .Throws<DbUpdateException>();

            db.ChangeTracker.Clear();
            var incomplete = UserWithoutIdentity(Guid.CreateVersion7(now.AddSeconds(3)), "sso-incomplete", now);
            incomplete.ExternalIdentityProviderId = providerId;
            db.Users.Add(incomplete);
            await Assert.That(async () => await db.SaveChangesAsync(cancellationToken))
                .Throws<DbUpdateException>();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Data_protection_keys_are_encrypted_and_shared_between_instances(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_sso_keys", cancellationToken);
            await using (var db = new NoCtfDbContext(OptionsFor(postgres.ConnectionString)))
                await db.Database.MigrateAsync(cancellationToken);

            var encryptionKey = Convert.ToBase64String(
                Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
            await using var first = BuildDataProtectionServices(postgres.ConnectionString, encryptionKey);
            var payload = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("NoCTF.Tests.Sso")
                .Protect("cross-instance");

            await using (var db = new NoCtfDbContext(OptionsFor(postgres.ConnectionString)))
            {
                var stored = await db.DataProtectionKeys.AsNoTracking().SingleAsync(cancellationToken);
                await Assert.That(stored.Xml).StartsWith("noctf-sso-dp-v1:");
                await Assert.That(stored.Xml).DoesNotContain("masterKey");
            }

            await using var second = BuildDataProtectionServices(postgres.ConnectionString, encryptionKey);
            var restored = second.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("NoCTF.Tests.Sso")
                .Unprotect(payload);
            await Assert.That(restored).IsEqualTo("cross-instance");
        });
    }

    private static ServiceProvider BuildDataProtectionServices(
        string connectionString,
        string encryptionKey)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NoCtfDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddSingleton<IOptions<EmailVerificationProtectionOptions>>(
            Options.Create(new EmailVerificationProtectionOptions
            {
                EncryptionKey = encryptionKey
            }));
        services.AddSingleton<PlatformSecretProtector>();
        services.AddSingleton<IXmlRepository, PostgresEncryptedDataProtectionKeyRepository>();
        services.AddDataProtection()
            .SetApplicationName("NoCTF");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IXmlRepository>((options, repository) => options.XmlRepository = repository);
        return services.BuildServiceProvider();
    }

    private static User UserWithIdentity(
        Guid id,
        string userName,
        Guid providerId,
        string subject,
        DateTimeOffset now)
    {
        var user = UserWithoutIdentity(id, userName, now);
        user.ExternalIdentityProviderId = providerId;
        user.ExternalIdentityProtocol = SsoProtocol.Oidc;
        user.ExternalIdentityNamespace = "https://issuer.example.test";
        user.ExternalIdentitySubject = subject;
        user.ExternalIdentityBoundAt = now;
        return user;
    }

    private static User UserWithoutIdentity(Guid id, string userName, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = $"{userName}@example.test",
        PasswordHash = "unused",
        Kind = UserKind.Human,
        Role = UserRole.User,
        AccountStatus = UserAccountStatus.Active,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<PostgresLease> StartPostgresAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var external = Environment.GetEnvironmentVariable("NOCTF_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var adminBuilder = new NpgsqlConnectionStringBuilder(external)
            {
                Database = "postgres"
            };
            var databaseBuilder = new NpgsqlConnectionStringBuilder(external)
            {
                Database = database
            };
            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {database}";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new(databaseBuilder.ConnectionString, null, adminBuilder.ConnectionString, database);
        }

        var postgres = new PostgreSqlBuilder(Image)
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return new(postgres.GetConnectionString(), postgres, null, null);
    }

    private static DbContextOptions<NoCtfDbContext> OptionsFor(string connectionString) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed class PostgresLease(
        string connectionString,
        PostgreSqlContainer? container,
        string? adminConnectionString,
        string? database) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
            {
                await container.DisposeAsync();
                return;
            }

            if (adminConnectionString is null || database is null)
                return;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE {database} WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
    }
}
