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
        "data_exports",
        "files",
        "notifications",
        "patch_uploads",
        "platform_settings",
        "runtime_instances",
        "scoring_events",
        "submissions",
        "teams",
        "users"
    ];

    [Test]
    [Category("Integration")]
    public async Task Initial_baseline_contains_exactly_the_seventeen_business_tables(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
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
        });
    }
}
