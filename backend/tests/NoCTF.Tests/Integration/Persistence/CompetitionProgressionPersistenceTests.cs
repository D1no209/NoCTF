using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionProgressionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Migration_and_tph_graph_round_trip_preserve_revision_and_guard_concurrency(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_progression")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.Parse("2026-09-25T00:00:00Z");
            var ownerId = Guid.NewGuid();
            var competitionId = Guid.NewGuid();
            var challengeId = Guid.NewGuid();
            var instanceId = Guid.NewGuid();
            var badgeId = Guid.NewGuid();
            var nodeId = Guid.NewGuid();
            var badgeNodeId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                await Assert.That(await setup.Database.GetPendingMigrationsAsync(ct)).IsEmpty();
                setup.Users.Add(new User
                {
                    Id = ownerId, UserName = "owner", Email = "owner@example.test",
                    PasswordHash = "test", Kind = UserKind.Human,
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Competitions.Add(new CtfCompetition
                {
                    Id = competitionId, OwnerId = ownerId, Title = "Graph",
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                    FlagDerivationSecret = new byte[32], StartAt = now,
                    EndAt = now.AddDays(1), Status = CompetitionStatus.Running,
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Challenges.Add(new CtfChallenge
                {
                    Id = challengeId, OwnerId = ownerId, Title = "Question",
                    Direction = "Web", Definition = TestConfigurations.Definition(GameMode.Ctf),
                    CreatedAt = now, UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new CtfCompetitionChallenge
                {
                    Id = instanceId, CompetitionId = competitionId, ChallengeId = challengeId,
                    IsPublished = true, Rules = TestConfigurations.Rules(GameMode.Ctf),
                    UpdatedAt = now
                });
                setup.Files.Add(new StoredFile
                {
                    Id = imageId, ObjectKey = "badges/graph.png",
                    FileName = "graph.png", ContentType = "image/png",
                    ByteLength = 1, Sha256 = new byte[32], CreatedAt = now
                });
                setup.CompetitionBadges.Add(new CompetitionBadge
                {
                    Id = badgeId, CompetitionId = competitionId, Name = "Badge",
                    ImageFileId = imageId, CreatedAt = now, UpdatedAt = now
                });
                await setup.SaveChangesAsync(ct);
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var store = new CompetitionProgressionStore(db, new ProgressionReconciler(db));
                var result = await store.SaveAsync(new(
                    competitionId, null, true, true,
                    [new(nodeId, ProgressionNodeKind.Challenge, instanceId, 10, 20),
                     new(badgeNodeId, ProgressionNodeKind.Badge, badgeId, 200, 20)],
                    [new(Guid.NewGuid(), nodeId, badgeNodeId,
                        ProgressionPrerequisiteCondition.Completed)], now), ct);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Progression!.Revision).IsEqualTo(1);
                await Assert.That(await db.ProgressionNodes.OfType<ChallengeProgressionNode>()
                    .CountAsync(ct)).IsEqualTo(1);
                await Assert.That(await db.ProgressionNodes.OfType<BadgeProgressionNode>()
                    .CountAsync(ct)).IsEqualTo(1);
                var stale = await store.SaveAsync(new(
                    competitionId, Guid.NewGuid(), false, false, [], [], now), ct);
                await Assert.That(stale.Failure)
                    .IsEqualTo(CompetitionProgressionSaveFailure.ConcurrencyConflict);
            }
        });
    }
}
