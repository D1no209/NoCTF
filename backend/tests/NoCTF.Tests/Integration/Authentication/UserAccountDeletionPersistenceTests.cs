using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class UserAccountDeletionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Deletion_preserves_referenced_history_and_physically_removes_unused_users(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_user_deletion")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var actorId = Guid.CreateVersion7(now);
            var referencedUserId = Guid.CreateVersion7(now.AddTicks(1));
            var unusedUserId = Guid.CreateVersion7(now.AddTicks(2));
            var auditedUserId = Guid.CreateVersion7(now.AddTicks(3));
            var detachableUserId = Guid.CreateVersion7(now.AddTicks(4));
            var competitionId = Guid.CreateVersion7(now.AddTicks(5));

            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                db.Users.AddRange(
                    User(actorId, "Admin", "admin@example.test", UserRole.Administrator, now),
                    User(referencedUserId, "Player", "player@example.test", UserRole.User, now),
                    User(unusedUserId, "Unused", "unused@example.test", UserRole.User, now),
                    User(auditedUserId, "Auditor", "auditor@example.test", UserRole.User, now),
                    User(detachableUserId, "Detachable", "detachable@example.test", UserRole.User, now));
                db.Notifications.Add(new MessageNotification
                {
                    Id = Guid.CreateVersion7(now.AddTicks(5)),
                    SourceType = NotificationSourceType.System,
                    SourceId = null,
                    TargetType = NotificationTargetType.User,
                    TargetId = auditedUserId,
                    SentAt = now
                });
                db.Notifications.Add(new AuthenticationSecurityActivityNotification
                {
                    Id = Guid.CreateVersion7(now.AddTicks(6)),
                    SourceType = NotificationSourceType.User,
                    SourceId = detachableUserId,
                    TargetType = NotificationTargetType.PlatformAdministrators,
                    TargetId = Notification.PlatformAdministratorsTargetId,
                    SentAt = now
                });
                db.Notifications.Add(new MessageNotification
                {
                    Id = Guid.CreateVersion7(now.AddTicks(7)),
                    SourceType = NotificationSourceType.System,
                    SourceId = null,
                    TargetType = NotificationTargetType.PlatformAdministrators,
                    TargetId = Notification.PlatformAdministratorsTargetId,
                    RelatedType = EntityReferenceKind.User,
                    RelatedId = detachableUserId,
                    SentAt = now
                });
                var challengeId = Guid.CreateVersion7(now.AddTicks(8));
                var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(9));
                db.Competitions.Add(new CtfCompetition
                {
                    Id = competitionId,
                    OwnerId = referencedUserId,
                    ManagerIds = [detachableUserId],
                    Title = "Historical competition",
                    Status = CompetitionStatus.Finished,
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                    MaxTeamMembers = 5,
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(-2),
                    EndAt = now.AddHours(-1),
                    CreatedAt = now.AddHours(-3),
                    UpdatedAt = now
                });
                db.Challenges.Add(new CtfChallenge
                {
                    Id = challengeId,
                    OwnerId = referencedUserId,
                    ManagerIds = [detachableUserId],
                    Title = "Historical challenge",
                    Visibility = ChallengeVisibility.Private,
                    Definition = TestConfigurations.Definition(GameMode.Ctf),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                db.CompetitionChallenges.Add(new CtfCompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    IsPublished = true,
                    Rules = TestConfigurations.Rules(GameMode.Ctf),
                    UpdatedAt = now
                });
                var teamId = Guid.CreateVersion7(now.AddTicks(10));
                db.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = competitionId,
                    Name = "Historical team",
                    CaptainId = referencedUserId,
                    MemberIds = [referencedUserId, detachableUserId],
                    InvitationToken = new string('a', 32),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                db.GameplayFacts.Add(new ManualAdjustmentGameplayFact
                {
                    Id = Guid.CreateVersion7(now.AddTicks(11)),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    ActorUserId = referencedUserId,
                    State = GameplayFactState.Completed,
                    Result = GameplayFactResult.Applied,
                    Value = "25",
                    OccurredAt = now.AddHours(-1),
                    UpdatedAt = now.AddHours(-1)
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new UserAccountAdministrationStore(db);
                var auditedPreview = await store.PreviewDeletionAsync(
                    auditedUserId,
                    actorId,
                    cancellationToken);
                var preview = await store.PreviewDeletionAsync(
                    referencedUserId,
                    actorId,
                    cancellationToken);
                var detachablePreview = await store.PreviewDeletionAsync(
                    detachableUserId,
                    actorId,
                    cancellationToken);
                var blocked = await store.DeleteAsync(
                    referencedUserId,
                    actorId,
                    UserDeletionMode.HardDelete,
                    "Requested account closure",
                    now.AddMinutes(1),
                    cancellationToken);
                var anonymized = await store.DeleteAsync(
                    referencedUserId,
                    actorId,
                    UserDeletionMode.Anonymize,
                    "Requested account closure",
                    now.AddMinutes(2),
                    cancellationToken);
                var deleted = await store.DeleteAsync(
                    unusedUserId,
                    actorId,
                    UserDeletionMode.HardDelete,
                    "Unused test account",
                    now.AddMinutes(3),
                    cancellationToken);
                var detached = await store.DeleteAsync(
                    detachableUserId,
                    actorId,
                    UserDeletionMode.HardDelete,
                    "Remove test collaborator",
                    now.AddMinutes(4),
                    cancellationToken);

                await Assert.That(auditedPreview!.CanHardDelete).IsFalse();
                await Assert.That(auditedPreview.References.Any(reference =>
                    reference.Kind == UserDeletionReferenceKind.Notification
                    && reference.Count == 1)).IsTrue();
                await Assert.That(preview!.CanHardDelete).IsFalse();
                await Assert.That(preview.References.Any(reference =>
                    reference.Kind == UserDeletionReferenceKind.CompetitionOwner
                    && reference.Count == 1)).IsTrue();
                await Assert.That(detachablePreview!.CanHardDelete).IsTrue();
                await Assert.That(detachablePreview.References.Select(reference => reference.Kind))
                    .IsEquivalentTo([
                        UserDeletionReferenceKind.CompetitionCollaborator,
                        UserDeletionReferenceKind.ChallengeManager,
                        UserDeletionReferenceKind.TeamMember
                    ]);
                await Assert.That(blocked.State).IsEqualTo(UserDeletionState.HardDeleteBlocked);
                await Assert.That(anonymized.State).IsEqualTo(UserDeletionState.Anonymized);
                await Assert.That(deleted.State).IsEqualTo(UserDeletionState.PhysicallyDeleted);
                await Assert.That(detached.State).IsEqualTo(UserDeletionState.PhysicallyDeleted);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var anonymized = await db.Users.SingleAsync(
                    user => user.Id == referencedUserId,
                    cancellationToken);
                await Assert.That(anonymized.AccountStatus)
                    .IsEqualTo(UserAccountStatus.Anonymized);
                await Assert.That(anonymized.UserName)
                    .IsEqualTo($"anonymous-{referencedUserId:N}");
                await Assert.That(anonymized.Email).IsEmpty();
                await Assert.That(anonymized.PasswordHash).IsEmpty();
                await Assert.That(anonymized.Role).IsEqualTo(UserRole.User);
                await Assert.That(await db.Competitions.IgnoreQueryFilters().AnyAsync(
                    competition => competition.Id == competitionId
                        && competition.OwnerId == referencedUserId,
                    cancellationToken)).IsTrue();
                await Assert.That(await db.Users.AnyAsync(
                    user => user.Id == unusedUserId,
                    cancellationToken)).IsFalse();
                await Assert.That(await db.Users.AnyAsync(
                    user => user.Id == detachableUserId,
                    cancellationToken)).IsFalse();
                await Assert.That(await db.Competitions.IgnoreQueryFilters().AnyAsync(
                    competition => competition.Collaborators.Any(collaborator =>
                        collaborator.Role == CompetitionCollaboratorRole.Manager
                        && collaborator.UserId == detachableUserId),
                    cancellationToken)).IsFalse();
                await Assert.That(await db.Challenges.IgnoreQueryFilters().AnyAsync(
                    challenge => challenge.Managers.Any(manager =>
                        manager.UserId == detachableUserId),
                    cancellationToken)).IsFalse();
                await Assert.That(await db.Teams.IgnoreQueryFilters().AnyAsync(
                    team => team.Members.Any(member =>
                        member.UserId == detachableUserId),
                    cancellationToken)).IsFalse();
                await Assert.That(await db.Notifications.CountAsync(cancellationToken))
                    .IsEqualTo(4);
                var lifecycleFacts = await new PlatformAuditLogStore(db).QueryAsync(
                    new(
                        PlatformAuditKind.UserAccountLifecycle,
                        null,
                        null,
                        null,
                        actorId,
                        null,
                        null,
                        10),
                    cancellationToken);
                await Assert.That(lifecycleFacts).Count().IsEqualTo(3);
                await Assert.That(lifecycleFacts[0].UserAccountAction)
                    .IsEqualTo(UserAccountLifecycleAction.PhysicallyDeleted);
                await Assert.That(lifecycleFacts[0].Reason).IsEqualTo("Remove test collaborator");
                await Assert.That(lifecycleFacts[1].UserAccountAction)
                    .IsEqualTo(UserAccountLifecycleAction.PhysicallyDeleted);
                await Assert.That(lifecycleFacts[1].Reason).IsEqualTo("Unused test account");
                await Assert.That(lifecycleFacts[2].UserAccountAction)
                    .IsEqualTo(UserAccountLifecycleAction.Anonymized);
                await Assert.That(lifecycleFacts[2].Reason)
                    .IsEqualTo("Requested account closure");
                await Assert.That(await new AccessTokenVersionReader(db).IsCurrentAsync(
                    referencedUserId,
                    0,
                    cancellationToken)).IsFalse();
            }
        });
    }

    private static User User(
        Guid id,
        string userName,
        string email,
        UserRole role,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            PasswordHash = "password-hash",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

}
