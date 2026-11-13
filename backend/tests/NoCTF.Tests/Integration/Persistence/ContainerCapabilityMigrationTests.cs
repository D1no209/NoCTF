using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ContainerCapabilityMigrationTests
{
    [Test, Timeout(300_000)]
    public async Task Upgrade_preserves_existing_material_and_marks_legacy_Control_as_unconfigured(CancellationToken ct)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
            database => database.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
        var userId = Guid.NewGuid(); var challengeId = Guid.NewGuid();
        await using (var baseline = new NoCtfDbContext(options))
        {
            await baseline.GetService<IMigrator>().MigrateAsync("20261105094930_InitialBaseline", ct);
            baseline.Users.Add(new() { Id = userId, UserName = "migration-author", NormalizedUserName = "MIGRATION-AUTHOR",
                Email = "author@example.test", NormalizedEmail = "AUTHOR@EXAMPLE.TEST", Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active });
            baseline.Challenges.Add(new() { Id = challengeId, OwnerId = userId, Title = "Preserved material", Direction = "Misc",
                Definition = new() { Capabilities = [new BreakCapabilityDefinition()] } });
            await baseline.SaveChangesAsync(ct);
        }
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                UPDATE challenge_capabilities SET kind = 3, protocol = 0, service_name = 'main', container_port = 8081,
                    url_template = 'http://{HOST}:{PORT}/control', control_capability_definition_timeout_seconds = 10
                WHERE challenge_id = @id
                """, connection);
            command.Parameters.AddWithValue("id", challengeId);
            await Assert.That(await command.ExecuteNonQueryAsync(ct)).IsEqualTo(1);
        }
        await using var upgraded = new NoCtfDbContext(options);
        await upgraded.Database.MigrateAsync(ct);
        var challenge = await upgraded.Challenges.AsSplitQuery().SingleAsync(item => item.Id == challengeId, ct);
        await Assert.That(challenge.Title).IsEqualTo("Preserved material");
        await Assert.That(challenge.OwnerId).IsEqualTo(userId);
        await Assert.That(challenge.Definition!.Control!.Image).IsNull();
        await Assert.That(challenge.Definition.Control.ServiceName).IsEqualTo("main");
        await Assert.That(challenge.Definition.Control.TimeoutSeconds).IsEqualTo(10);
        await Assert.That(ChallengeCapabilityValidation.Validate(challenge.Definition).Any(issue => issue.Capability == ChallengeCapabilityKind.Control)).IsTrue();
        await Assert.That((await upgraded.Database.GetAppliedMigrationsAsync(ct)).Count()).IsEqualTo(2);
        await Assert.That(await upgraded.Users.CountAsync(ct)).IsEqualTo(1);
    }
}
