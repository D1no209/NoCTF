using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration;
using Testcontainers.PostgreSql;
using Wolverine;

namespace NoCTF.Tests.Refactor;

public sealed class DataModelSchemaTests
{
    private static readonly string[] ExpectedTables =
    [
        "account_tokens",
        "challenge_attachments",
        "challenge_flags",
        "challenges",
        "competition_challenges",
        "competition_events",
        "competitions",
        "files",
        "gameplay_facts",
        "notifications",
        "patch_uploads",
        "platform_settings",
        "runtime_instances",
        "teams",
        "users"
    ];

    [Test]
    [Category("Integration")]
    public async Task Current_model_contains_exactly_the_fifteen_business_tables(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_model")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var db = new NoCtfDbContext(options))
                await db.Database.MigrateAsync(ct);

            await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND left(tablename, 2) <> '__' ORDER BY tablename",
                connection);
            var actual = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    actual.Add(reader.GetString(0));
            }

            await Assert.That(actual).IsEquivalentTo(ExpectedTables);

            await using var migrationCommand = new NpgsqlCommand(
                "SELECT migration_id FROM \"__EFMigrationsHistory\" ORDER BY migration_id",
                connection);
            var migrations = new List<string>();
            await using (var reader = await migrationCommand.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    migrations.Add(reader.GetString(0));
            }

            await Assert.That(migrations).Count().IsEqualTo(1);
            await Assert.That(migrations[0]).EndsWith("_InitialBaseline");

            await using var primaryKeyCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM pg_constraint
                WHERE connamespace = 'public'::regnamespace
                  AND contype = 'p'
                  AND conrelid::regclass::text = ANY(@tables)
                """,
                connection);
            primaryKeyCommand.Parameters.AddWithValue("tables", ExpectedTables);
            await Assert.That((int)(await primaryKeyCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(ExpectedTables.Length);

            await using var cascadingForeignKeyCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM pg_constraint
                WHERE connamespace = 'public'::regnamespace
                  AND contype = 'f'
                  AND confdeltype = 'c'
                """,
                connection);
            await Assert.That((int)(await cascadingForeignKeyCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(0);

            await using var forbiddenSchemaCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND (
                    table_name = 'data_exports'
                    OR column_name IN (
                        'revision',
                        'expected_revision',
                        'concurrency_token',
                        'leaderboard_dirty',
                        'generation',
                        'runner_pool',
                        'checker_deadline'))
                """,
                connection);
            await Assert.That((int)(await forbiddenSchemaCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(0);
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Initial_baseline_and_all_Wolverine_role_schemas_can_share_one_database(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_model_wolverine")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);

            var connectionString = postgres.GetConnectionString();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var db = new NoCtfDbContext(options))
                await db.Database.MigrateAsync(ct);

            foreach (var role in new[] { HostRole.Api, HostRole.Worker, HostRole.Runner })
            {
                var builder = Host.CreateApplicationBuilder();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSql"] = connectionString
                });
                builder.UseWolverine(wolverine =>
                {
                    wolverine.Discovery.DisableConventionalDiscovery();
                    wolverine.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    wolverine.ConfigureNoCtfPersistence(
                        builder.Configuration,
                        HostRoles.Only(role));
                });
                using var host = builder.Build();
                await host.StartAsync(ct);
                await host.StopAsync(ct);
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            foreach (var schema in WolverinePersistenceSchemas.All)
            {
                await using var schemaCommand = new NpgsqlCommand(
                    """
                    SELECT count(*)::int
                    FROM pg_tables
                    WHERE schemaname = @schema
                      AND tablename IN (
                        'wolverine_incoming_envelopes',
                        'wolverine_outgoing_envelopes',
                        'wolverine_nodes')
                    """,
                    connection);
                schemaCommand.Parameters.AddWithValue("schema", schema);
                await Assert.That((int)(await schemaCommand.ExecuteScalarAsync(ct))!)
                    .IsEqualTo(3);
            }
        });
    }
}
