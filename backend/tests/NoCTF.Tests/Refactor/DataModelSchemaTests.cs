using Microsoft.EntityFrameworkCore;
using Npgsql;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Refactor;

public sealed class DataModelSchemaTests
{
    private static readonly string[] RequiredTables =
    [
        "active_runtime_slots",
        "challenge_definitions",
        "challenge_runtime_templates",
        "challenges",
        "command_receipts",
        "competition_challenge_rules",
        "competition_challenges",
        "competition_collaborators",
        "competition_progressions",
        "competition_progression_nodes",
        "competition_progression_edges",
        "competition_badges",
        "competition_events",
        "competition_mode_configurations",
        "competitions",
        "external_identities",
        "gameplay_facts",
        "human_verification_turnstile_hostnames",
        "notifications",
        "platform_settings",
        "runtime_access_endpoints",
        "runtime_capacity_allocations",
        "runtime_instances",
        "team_members",
        "team_progression_node_states",
        "team_progression_badge_states",
        "user_badge_grants",
        "user_badge_transitions",
        "teams",
        "users"
    ];

    [Test]
    [Category("Integration")]
    public async Task Current_model_uses_only_relational_portable_business_storage(
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
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
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

            await Assert.That(RequiredTables.All(actual.Contains)).IsTrue();
            await Assert.That(actual).Contains("data_protection_keys");

            await using var migrationCommand = new NpgsqlCommand(
                "SELECT migration_id FROM \"__EFMigrationsHistory\" ORDER BY migration_id",
                connection);
            var migrations = new List<string>();
            await using (var reader = await migrationCommand.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    migrations.Add(reader.GetString(0));
            }

            await Assert.That(migrations).Count().IsEqualTo(2);
            await Assert.That(migrations[1]).EndsWith("_CompetitionProgression");
            await Assert.That(migrations[0]).EndsWith("_InitialBaseline");

            await using var removedGatewayColumnsCommand = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'platform_settings' AND column_name LIKE 'public_gateway_%'",
                connection);
            var removedGatewayColumns = (long)(await removedGatewayColumnsCommand.ExecuteScalarAsync(ct))!;
            await Assert.That(removedGatewayColumns).IsEqualTo(0);

            await using var accessModeColumnCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'competitions'
                  AND column_name = 'access_mode'
                  AND is_nullable = 'NO'
                  AND data_type = 'smallint'
                """,
                connection);
            await Assert.That((int)(await accessModeColumnCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(1);

            await using var writeUpPolicyColumnCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'competitions'
                  AND column_name IN ('write_up_submission_required', 'write_up_submission_deadline_hours')
                  AND is_nullable = 'NO'
                """,
                connection);
            await Assert.That((int)(await writeUpPolicyColumnCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(2);

            await using var primaryKeyCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM pg_constraint
                WHERE connamespace = 'public'::regnamespace
                  AND contype = 'p'
                  AND conrelid::regclass::text = ANY(@tables)
                """,
                connection);
            primaryKeyCommand.Parameters.AddWithValue("tables", actual.ToArray());
            await Assert.That((int)(await primaryKeyCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(actual.Count);

            await using var nonPortableColumnCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND (data_type IN ('ARRAY', 'json', 'jsonb')
                    OR udt_name IN ('json', 'jsonb')
                    OR column_name LIKE '%\_json' ESCAPE '\')
                """,
                connection);
            await Assert.That((int)(await nonPortableColumnCommand.ExecuteScalarAsync(ct))!)
                .IsEqualTo(0);

            await using var forbiddenSchemaCommand = new NpgsqlCommand(
                """
                SELECT count(*)::int
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND (
                    table_name = 'data_exports'
                    OR (column_name = 'revision'
                        AND table_name <> 'competition_progressions')
                    OR column_name IN (
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

}
