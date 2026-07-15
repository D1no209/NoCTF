using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoCTF.Infrastructure;
using Npgsql;

namespace NoCTF.Tests;

public sealed class UserIdentityMigrationTests
{
    private const string PreviousMigration = "20260710115346_BackendAuditRemediation";
    private const string IdentityMigration = "20260712054600_BackendArchitecturePerformance";
    private const string TestPasswordHash = "$2a$test";

    [Fact]
    public async Task BackendArchitecturePerformance_PreflightsLegacyUserIdentitiesBeforeDdl()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_POSTGRES_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var schemaName = $"noctf_identity_migration_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(connectionString);
        await adminConnection.OpenAsync();
        await using (var createSchema = adminConnection.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schemaName)};";
            await createSchema.ExecuteNonQueryAsync();
        }

        var isolatedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schemaName,
            IncludeErrorDetail = true
        }.ConnectionString;
        await using var db = CreatePostgresDb(isolatedConnectionString, schemaName);
        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await InsertUserAsync(db, " Alice ", "Case@Test.Example");
            await InsertUserAsync(db, "alice", "case@test.example ");
            await InsertUserAsync(db, new string('u', 65), new string('e', 255));

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => migrator.MigrateAsync(IdentityMigration));

            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Contains("legacy Users violate", exception.MessageText, StringComparison.Ordinal);
            Assert.Contains("overlong emails=1", exception.Detail, StringComparison.Ordinal);
            Assert.Contains("overlong user names=1", exception.Detail, StringComparison.Ordinal);
            Assert.Contains("normalized email collision groups=1", exception.Detail, StringComparison.Ordinal);
            Assert.Contains("normalized user-name collision groups=1", exception.Detail, StringComparison.Ordinal);
            Assert.Contains("then rerun the migration", exception.Hint, StringComparison.Ordinal);
            Assert.DoesNotContain("Case@Test.Example", exception.ToString(), StringComparison.Ordinal);

            var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.DoesNotContain(IdentityMigration, appliedMigrations);
            Assert.Equal(
                "text",
                await ExecuteScalarStringAsync(
                    isolatedConnectionString,
                    """
                    SELECT data_type
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'Users'
                      AND column_name = 'Email';
                    """));
            Assert.Contains(
                "(\"Email\")",
                await ExecuteScalarStringAsync(
                    isolatedConnectionString,
                    """
                    SELECT indexdef
                    FROM pg_indexes
                    WHERE schemaname = current_schema()
                      AND indexname = 'ix_users_email';
                    """),
                StringComparison.Ordinal);

            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Users\";");
            await InsertUserAsync(db, "ValidUser", "Valid@Example.com");
            await migrator.MigrateAsync(IdentityMigration);

            appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(IdentityMigration, appliedMigrations);
            Assert.Equal(
                "character varying",
                await ExecuteScalarStringAsync(
                    isolatedConnectionString,
                    """
                    SELECT data_type
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'Users'
                      AND column_name = 'Email';
                    """));

            var duplicate = await Assert.ThrowsAsync<PostgresException>(
                () => InsertUserAsync(db, "AnotherUser", " valid@example.com "));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            Assert.Equal("ix_users_email", duplicate.ConstraintName);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
            await using var dropSchema = adminConnection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static ApplicationDbContext CreatePostgresDb(string connectionString, string schemaName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
            .Options;
        return new ApplicationDbContext(options, new TenantContext());
    }

    private static Task<int> InsertUserAsync(
        ApplicationDbContext db,
        string userName,
        string email)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Users"
                ("Id", "UserName", "Email", "PasswordHash", "Role", "TokenVersion", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {userName}, {email}, {TestPasswordHash}, 2, 0, {DateTime.UtcNow}, {DateTime.UtcNow});
            """);

    private static async Task<string> ExecuteScalarStringAsync(
        string connectionString,
        string commandText)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
