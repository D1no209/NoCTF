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
            // Stage 10 owns the EF-generated InitialBaseline. Until then, validate
            // the current model directly without treating the legacy migration chain
            // as the target schema.
            await using (var db = new NoCtfDbContext(options))
                await db.Database.EnsureCreatedAsync(ct);

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
