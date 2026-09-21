using Microsoft.EntityFrameworkCore;
using Npgsql;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration;
using Testcontainers.PostgreSql;

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

            await Assert.That(actual.Where(table => table != "data_protection_keys"))
                .IsEquivalentTo(ExpectedTables);
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

            await Assert.That(migrations).IsEquivalentTo(
            [
                "20260826172216_InitialBaseline",
                "20260904120421_ChallengeTemplateTestRuntimes",
                "20260907190934_PrivateSchoolIdentityAndSourceAddresses",
                "20260908040028_OptionalPublicGateway",
                "20260908161132_PracticeOnlyTeams",
                "20260910145118_UserWallpaperPreferences",
                "20260911081054_RemovePracticeTeamMarker",
                "20260911144210_AddHumanVerificationToggle",
                "20260911164825_AddManagedHumanVerificationProviders",
                "20260911213004_AddCompetitionTracksEnabled",
                "20260912074543_AddRuntimeHumanVerificationToggle",
            "20260912130511_AddCompetitionAccessMode",
            "20260912150416_AddTeamWriteUps",
            "20260912182604_ConfigureTeamWriteUpSubmission",
            "20260913172544_AddHumanVerificationEvaluationToggle",
            "20260914154253_AddCtfPatchVerificationExperiment",
            "20260918101546_AddRuntimeCapacityAllocations",
            "20260920033821_AddSsoFoundation",
            "20260920150729_AllowBotAdministratorRole",
            "20260920151129_AddCompetitionWebhooks"
        ]);
            await Assert.That(migrations[0]).EndsWith("_InitialBaseline");

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

}
