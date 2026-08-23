using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Messaging;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ConfigurationLastWriteWinsTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Cross_configuration_updates_apply_without_revision_fences(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var ids = await SeedAsync(options, cancellationToken);

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await new CompetitionConfigurationStore(
                    updateDb,
                    new OpenApiTransactionalMessageOutbox()).TryUpdateAsync(
                    ids.CompetitionId,
                    """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":600,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""",
                    true,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Configuration).IsNotNull();
            }

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await new ChallengeConfigurationStore(
                    updateDb,
                    new OpenApiTransactionalMessageOutbox()).TryUpdateAsync(
                    ids.CompetitionId,
                    ids.CompetitionChallengeId,
                    """{"schemaVersion":2,"scoreCurve":{"initialPoints":700,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Configuration).IsNotNull();
            }

            await using var verifyDb = new NoCtfDbContext(options);
            var persisted = await verifyDb.CompetitionChallenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == ids.CompetitionChallengeId, cancellationToken);
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(persisted.RulesJson),
                JsonNode.Parse("""{"schemaVersion":2,"scoreCurve":{"initialPoints":700,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}"""))).IsTrue();
            var persistedCompetition = await verifyDb.Competitions.AsNoTracking()
                .SingleAsync(competition => competition.Id == ids.CompetitionId, cancellationToken);
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(persistedCompetition.ConfigurationJson),
                JsonNode.Parse("""{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":600,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}"""))).IsTrue();
        });
    }

    private static async Task<FixtureIds> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Configuration fences",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":2}""",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Challenge",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            RulesJson = """{"schemaVersion":2}""",
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(competitionId, competitionChallengeId);
    }

    private sealed record FixtureIds(Guid CompetitionId, Guid CompetitionChallengeId);
}
