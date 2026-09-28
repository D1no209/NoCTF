using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
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
            var otherUserId = Guid.NewGuid();
            var competitionId = Guid.NewGuid();
            var challengeId = Guid.NewGuid();
            var instanceId = Guid.NewGuid();
            var badgeId = Guid.NewGuid();
            var nodeId = Guid.NewGuid();
            var badgeNodeId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            var otherTeamId = Guid.NewGuid();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                await Assert.That(await setup.Database.GetPendingMigrationsAsync(ct)).IsEmpty();
                setup.Users.Add(new User
                {
                    Id = ownerId, UserName = "owner", NormalizedUserName = "OWNER",
                    Email = "owner@example.test",
                    PasswordHash = "test", Kind = UserKind.Human,
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Users.Add(new User
                {
                    Id = otherUserId, UserName = "other", NormalizedUserName = "OTHER",
                    Email = "other@example.test",
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
                setup.Teams.Add(new Team
                {
                    Id = teamId, CompetitionId = competitionId, Name = "Team",
                    CaptainId = ownerId, MemberIds = [ownerId],
                    InvitationToken = Guid.NewGuid().ToString("N"),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                setup.Teams.Add(new Team
                {
                    Id = otherTeamId, CompetitionId = competitionId, Name = "Other team",
                    CaptainId = otherUserId, MemberIds = [otherUserId],
                    InvitationToken = Guid.NewGuid().ToString("N"),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                await setup.SaveChangesAsync(ct);
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var store = new CompetitionProgressionStore(db, new ProgressionReconciler(db));
                var empty = await store.SaveAsync(new(
                    competitionId, null, false, false, [], [], now), ct);
                await Assert.That(empty.Failure).IsNull();
                db.ChangeTracker.Clear();
                var result = await store.SaveAsync(new(
                    competitionId, empty.Progression!.ConcurrencyStamp, true, true,
                    [new(nodeId, ProgressionNodeKind.Challenge, instanceId, true),
                     new(badgeNodeId, ProgressionNodeKind.Badge, badgeId, false)],
                    [new(Guid.NewGuid(), nodeId, badgeNodeId,
                        ProgressionPrerequisiteCondition.Completed)], now), ct);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Progression!.Revision).IsEqualTo(2);
                await Assert.That((await store.ReadAsync(competitionId, ct))!.Nodes
                    .Single(node => node.Id == badgeNodeId).RequiresPrerequisites).IsFalse();
                await Assert.That(await db.UserBadgeGrants.AsNoTracking()
                    .CountAsync(grant => grant.Active, ct)).IsEqualTo(2);
                await Assert.That(await db.ProgressionNodes.OfType<ChallengeProgressionNode>()
                    .CountAsync(ct)).IsEqualTo(1);
                await Assert.That(await db.ProgressionNodes.OfType<BadgeProgressionNode>()
                    .CountAsync(ct)).IsEqualTo(1);
                var starter = new ProgressionChallengeStarter(new TestContextFactory(options));
                await Assert.That(await starter.StartAsync(competitionId, instanceId, ownerId,
                    result.Progression.Revision, now.AddMinutes(1), ct))
                    .IsEqualTo(StartProgressionChallengeResult.Started);
                await Assert.That(await starter.StartAsync(competitionId, instanceId, ownerId,
                    result.Progression.Revision, now.AddMinutes(2), ct))
                    .IsEqualTo(StartProgressionChallengeResult.Started);
                await Assert.That(await db.TeamProgressionNodeVisits.AsNoTracking()
                    .Where(visit => visit.TeamId == teamId && visit.NodeId == nodeId)
                    .Select(visit => visit.FirstOpenedAt).SingleAsync(ct))
                    .IsEqualTo(now.AddMinutes(1));
                var player = await new ProgressionPlayerReader(db).ReadAsync(
                    competitionId, teamId, ct);
                await Assert.That(player.Nodes.Single(node => node.Id == nodeId).Visited).IsTrue();
                var other = await new ProgressionPlayerReader(db).ReadAsync(
                    competitionId, otherTeamId, ct);
                await Assert.That(other.Nodes.Single(node => node.Id == nodeId).Visited).IsFalse();
                var concurrentStarts = await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => starter.StartAsync(competitionId, instanceId, otherUserId,
                        result.Progression.Revision, now.AddMinutes(2), ct)));
                await Assert.That(concurrentStarts.All(start =>
                    start == StartProgressionChallengeResult.Started)).IsTrue();
                await Assert.That(await db.TeamProgressionNodeVisits.AsNoTracking()
                    .CountAsync(visit => visit.TeamId == otherTeamId && visit.NodeId == nodeId, ct))
                    .IsEqualTo(1);
                await Assert.That(await starter.StartAsync(competitionId, instanceId, ownerId,
                    result.Progression.Revision - 1, now.AddMinutes(3), ct))
                    .IsEqualTo(StartProgressionChallengeResult.GraphChanged);
                var stale = await store.SaveAsync(new(
                    competitionId, Guid.NewGuid(), false, false, [], [], now), ct);
                await Assert.That(stale.Failure)
                    .IsEqualTo(CompetitionProgressionSaveFailure.ConcurrencyConflict);
            }
        });
    }

    private sealed class TestContextFactory(DbContextOptions<NoCtfDbContext> options)
        : IDbContextFactory<NoCtfDbContext>
    {
        public NoCtfDbContext CreateDbContext() => new(options);

        public Task<NoCtfDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
