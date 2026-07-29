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
public sealed class ConfigurationRevisionFenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Cross_configuration_revision_changes_are_rejected_without_writes(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
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

            CompetitionConfigurationView competitionSnapshot;
            await using (var snapshotDb = new NoCtfDbContext(options))
                competitionSnapshot = (await new CompetitionConfigurationStore(
                    snapshotDb,
                    new OpenApiTransactionalMessageOutbox())
                    .FindAsync(ids.CompetitionId, cancellationToken))!;

            await using (var mutationDb = new NoCtfDbContext(options))
            {
                await mutationDb.CompetitionChallenges
                    .Where(challenge => challenge.Id == ids.CompetitionChallengeId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        challenge => challenge.Revision, challenge => challenge.Revision + 1), cancellationToken);
            }

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await new CompetitionConfigurationStore(
                    updateDb,
                    new OpenApiTransactionalMessageOutbox()).TryUpdateAsync(
                    ids.CompetitionId,
                    competitionSnapshot.Revision,
                    """{"schemaVersion":1,"defaultPoints":{"initialPoints":600,"minimumPoints":100,"decayFactor":10},"bloodRewards":[]}""",
                    true,
                    competitionSnapshot.ChallengeConfigurations.ToDictionary(
                        challenge => challenge.Id, challenge => challenge.Revision),
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(result.Failure).IsEqualTo(CompetitionConfigurationUpdateFailure.RevisionConflict);
            }

            ChallengeConfigurationView challengeSnapshot;
            await using (var snapshotDb = new NoCtfDbContext(options))
                challengeSnapshot = (await new ChallengeConfigurationStore(
                    snapshotDb,
                    new OpenApiTransactionalMessageOutbox())
                    .FindAsync(ids.CompetitionId, ids.CompetitionChallengeId, cancellationToken))!;

            await using (var mutationDb = new NoCtfDbContext(options))
            {
                await mutationDb.Competitions
                    .Where(competition => competition.Id == ids.CompetitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        competition => competition.ConfigurationRevision,
                        competition => competition.ConfigurationRevision + 1), cancellationToken);
            }

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await new ChallengeConfigurationStore(
                    updateDb,
                    new OpenApiTransactionalMessageOutbox()).TryUpdateAsync(
                    ids.CompetitionId,
                    ids.CompetitionChallengeId,
                    challengeSnapshot.Revision,
                    challengeSnapshot.CompetitionConfigurationRevision,
                    """{"schemaVersion":1,"points":{"initialPoints":700,"minimumPoints":100,"decayFactor":10},"bloodRewards":[]}""",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(result.Failure).IsEqualTo(ChallengeConfigurationUpdateFailure.RevisionConflict);
            }

            await using var verifyDb = new NoCtfDbContext(options);
            var persisted = await verifyDb.CompetitionChallenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == ids.CompetitionChallengeId, cancellationToken);
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(persisted.RulesJson),
                JsonNode.Parse("""{"schemaVersion":1}"""))).IsTrue();
            var persistedCompetition = await verifyDb.Competitions.AsNoTracking()
                .SingleAsync(competition => competition.Id == ids.CompetitionId, cancellationToken);
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(persistedCompetition.ConfigurationJson),
                JsonNode.Parse("""{"schemaVersion":1}"""))).IsTrue();
        });
    }

    private static async Task<FixtureIds> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
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
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
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
            ConfigurationJson = """{"schemaVersion":1}""",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Challenge",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            RulesJson = """{"schemaVersion":1}""",
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(competitionId, competitionChallengeId);
    }

    private sealed record FixtureIds(Guid CompetitionId, Guid CompetitionChallengeId);
}
