using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Unit.Competitions;

public sealed class ProgressionStoreRelationalTests
{
    [Test]
    public async Task Graph_save_reconciles_badge_award_revocation_and_reactivation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseSqlite(connection).Options;
        var now = DateTimeOffset.Parse("2026-09-25T00:00:00Z");
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var secondChallengeId = Guid.NewGuid();
        var competitionChallengeId = Guid.NewGuid();
        var secondInstanceId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var secondTeamId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var badgeId = Guid.NewGuid();
        var challengeNodeId = Guid.NewGuid();
        var badgeNodeId = Guid.NewGuid();
        var secondNodeId = Guid.NewGuid();
        var edgeId = Guid.NewGuid();
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User
        {
            Id = userId, UserName = "player", NormalizedUserName = "PLAYER",
            Email = "player@example.test", NormalizedEmail = "PLAYER@EXAMPLE.TEST",
            PasswordHash = "test", Kind = UserKind.Human,
            AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now
        });
        db.Users.Add(new User
        {
            Id = secondUserId, UserName = "second-player",
            NormalizedUserName = "SECOND-PLAYER",
            Email = "second@example.test", NormalizedEmail = "SECOND@EXAMPLE.TEST",
            PasswordHash = "test", Kind = UserKind.Human,
            AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now
        });
        db.Competitions.Add(new CtfCompetition
        {
            Id = competitionId, OwnerId = userId, Title = "Progression test",
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32], StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
            CreatedAt = now, UpdatedAt = now
        });
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId, OwnerId = userId, Title = "Challenge",
            Direction = "Web", Definition = TestConfigurations.Definition(GameMode.Ctf),
            CreatedAt = now, UpdatedAt = now
        });
        db.Challenges.Add(new CtfChallenge
        {
            Id = secondChallengeId, OwnerId = userId, Title = "Locked challenge",
            Direction = "Web", Definition = TestConfigurations.Definition(GameMode.Ctf),
            CreatedAt = now, UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId, CompetitionId = competitionId,
            ChallengeId = challengeId, IsPublished = true,
            Rules = TestConfigurations.Rules(GameMode.Ctf), UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = secondInstanceId, CompetitionId = competitionId,
            ChallengeId = secondChallengeId, IsPublished = true, Order = 1,
            Rules = TestConfigurations.Rules(GameMode.Ctf), UpdatedAt = now
        });
        var imageId = Guid.NewGuid();
        db.Files.Add(new StoredFile
        {
            Id = imageId, ObjectKey = "badges/test.png", FileName = "test.png",
            ContentType = "image/png", ByteLength = 4, Sha256 = new byte[32],
            CreatedAt = now
        });
        db.CompetitionBadges.Add(new CompetitionBadge
        {
            Id = badgeId, CompetitionId = competitionId, Name = "First gate",
            ImageFileId = imageId, CreatedAt = now, UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId, CompetitionId = competitionId, Name = "Players",
            CaptainId = userId, MemberIds = [userId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Teams.Add(new Team
        {
            Id = secondTeamId, CompetitionId = competitionId, Name = "Second",
            CaptainId = secondUserId, MemberIds = [secondUserId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        await db.SaveChangesAsync();

        using var cacheServices = new ServiceCollection()
            .AddFusionCache(NoCtfCacheNames.ReadModels).Services.BuildServiceProvider();
        var graphCache = new ProgressionGraphReadCache(
            cacheServices.GetRequiredService<IFusionCacheProvider>());
        var store = new CompetitionProgressionStore(
            db, new ProgressionReconciler(db), graphs: graphCache);
        var draft = new SaveCompetitionProgressionCommand(
            competitionId, null, true, false,
            [new(challengeNodeId, ProgressionNodeKind.Challenge,
                competitionChallengeId, 0, 0),
                new(badgeNodeId, ProgressionNodeKind.Badge, badgeId, 200, 0),
                new(secondNodeId, ProgressionNodeKind.Challenge, secondInstanceId, 400, 0)],
            [new(edgeId, challengeNodeId, badgeNodeId,
                ProgressionPrerequisiteCondition.Completed),
             new(Guid.NewGuid(), badgeNodeId, secondNodeId,
                ProgressionPrerequisiteCondition.Completed)], now);
        var first = await store.SaveAsync(draft, CancellationToken.None);
        await Assert.That(first.Failure).IsNull();
        await Assert.That(await db.TeamProgressionNodeStates.CountAsync())
            .IsEqualTo(6);
        await Assert.That(await db.UserBadgeGrants.CountAsync()).IsEqualTo(0);
        var access = new ProgressionChallengeAccess(db, graphCache);
        await Assert.That((await graphCache.ReadAsync(db, competitionId,
            CancellationToken.None))!.Revision).IsEqualTo(1);
        await Assert.That(await access.IsActiveAsync(
            competitionId, secondInstanceId, teamId, CancellationToken.None)).IsFalse();

        db.GameplayFacts.Add(new FlagAttemptGameplayFact
        {
            Id = Guid.NewGuid(), CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId, TeamId = teamId,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Correct,
            OccurredAt = now.AddMinutes(1), UpdatedAt = now.AddMinutes(1)
        });
        await db.SaveChangesAsync();
        await new ProgressionReconciler(db).ReconcileTeamAsync(
            competitionId, teamId,
            await LoadGraphAsync(db, competitionId), now.AddMinutes(1), null,
            CancellationToken.None);
        await db.SaveChangesAsync();
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(1);
        await new ProgressionReconciler(db).ReconcileTeamAsync(
            competitionId, teamId,
            await LoadGraphAsync(db, competitionId), now.AddMinutes(1), null,
            CancellationToken.None);
        await db.SaveChangesAsync();
        await Assert.That(await db.UserBadgeTransitions.CountAsync()).IsEqualTo(1);
        await Assert.That(await access.IsActiveAsync(
            competitionId, secondInstanceId, teamId, CancellationToken.None)).IsTrue();

        var disabled = await store.SaveAsync(draft with
        {
            ExpectedConcurrencyStamp = first.Progression!.ConcurrencyStamp,
            Enabled = false,
            Now = now.AddMinutes(2)
        }, CancellationToken.None);
        await Assert.That(disabled.Failure).IsNull();
        await Assert.That((await graphCache.ReadAsync(db, competitionId,
            CancellationToken.None))!.Enabled).IsFalse();
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(0);
        await Assert.That(await access.IsActiveAsync(
            competitionId, secondInstanceId, teamId, CancellationToken.None)).IsTrue();
        var reenabled = await store.SaveAsync(draft with
        {
            ExpectedConcurrencyStamp = disabled.Progression!.ConcurrencyStamp,
            Now = now.AddMinutes(3)
        }, CancellationToken.None);
        await Assert.That(reenabled.Failure).IsNull();
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(1);
        await Assert.That(await db.UserBadgeTransitions.CountAsync()).IsEqualTo(3);
        var lateMemberId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = lateMemberId, UserName = "late-player",
            NormalizedUserName = "LATE-PLAYER",
            Email = "late@example.test", NormalizedEmail = "LATE@EXAMPLE.TEST",
            Kind = UserKind.Human, AccountStatus = UserAccountStatus.Active,
            CreatedAt = now, UpdatedAt = now
        });
        var team = await db.Teams.SingleAsync(item => item.Id == teamId);
        team.MemberIds = [userId, lateMemberId];
        await db.SaveChangesAsync();
        await new ProgressionReconciler(db).ReconcilePersistedTeamAsync(
            competitionId, teamId, now.AddMinutes(3).AddTicks(1), CancellationToken.None);
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(1);
        team.RegistrationStatus = TeamRegistrationStatus.Pending;
        await db.SaveChangesAsync();
        await new ProgressionReconciler(db).ReconcilePersistedTeamAsync(
            competitionId, teamId, now.AddMinutes(3).AddTicks(2), CancellationToken.None);
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(0);
        team.RegistrationStatus = TeamRegistrationStatus.Approved;
        await db.SaveChangesAsync();
        await new ProgressionReconciler(db).ReconcilePersistedTeamAsync(
            competitionId, teamId, now.AddMinutes(3).AddTicks(3), CancellationToken.None);
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(2);
        var removed = await store.SaveAsync(draft with
        {
            ExpectedConcurrencyStamp = reenabled.Progression!.ConcurrencyStamp,
            Nodes = [draft.Nodes[0]], Edges = [],
            Now = now.AddMinutes(4)
        }, CancellationToken.None);
        await Assert.That(removed.Failure).IsNull();
        await Assert.That(await db.UserBadgeGrants.CountAsync(grant => grant.Active))
            .IsEqualTo(0);
        await Assert.That(await db.UserBadgeTransitions.CountAsync()).IsEqualTo(8);
    }

    private static Task<CompetitionProgression> LoadGraphAsync(
        NoCtfDbContext db, Guid competitionId) => db.CompetitionProgressions
        .Include(item => item.Nodes).Include(item => item.Edges)
        .AsSplitQuery().SingleAsync(item => item.CompetitionId == competitionId);
}
