using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Permissions;
using NoCTF.Infrastructure.Competitions.Visibility;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionNotificationDelivery")]
public sealed class CompetitionNotificationDeliveryPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Foreign_flag_detection_notifies_only_evidence_readers_and_is_idempotent(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_cheat_incident_notifications")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var competitionId = Guid.CreateVersion7();
            var administratorId = Guid.CreateVersion7();
            var ownerId = Guid.CreateVersion7();
            var managerId = Guid.CreateVersion7();
            var judgeId = Guid.CreateVersion7();
            var observerId = Guid.CreateVersion7();
            var participantId = Guid.CreateVersion7();
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            db.Users.AddRange(
                Human(administratorId, "incident-administrator", UserRole.Administrator, now),
                Human(ownerId, "incident-owner", UserRole.Organizer, now),
                Human(managerId, "incident-manager", UserRole.Organizer, now),
                Human(judgeId, "incident-judge", UserRole.Organizer, now),
                Human(observerId, "incident-observer", UserRole.Organizer, now),
                Human(participantId, "incident-participant", UserRole.User, now));
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                ManagerIds = [managerId],
                JudgeIds = [judgeId],
                ObserverIds = [observerId],
                Title = "Cheat incident notifications",
                Mode = GameMode.Ctf,
                ConfigurationUpdatedAt = now,
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1),
                EndAt = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);

            var message = new ForeignTeamFlagDetected(
                competitionId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                participantId,
                Guid.CreateVersion7(),
                now);
            var delivery = new CompetitionNotificationDelivery(db);
            await CompetitionNotificationMessageHandlers.Handle(message, db, delivery, ct);
            await CompetitionNotificationMessageHandlers.Handle(message, db, delivery, ct);

            var notifications = await db.Notifications.AsNoTracking().ToArrayAsync(ct);
            await Assert.That(notifications).Count().IsEqualTo(4);
            await Assert.That(notifications.Select(item => item.TargetId)).IsEquivalentTo([
                administratorId,
                ownerId,
                managerId,
                judgeId
            ]);
            await Assert.That(notifications.Select(item => item.TargetId)).DoesNotContain(observerId);
            await Assert.That(notifications.Select(item => item.TargetId)).DoesNotContain(participantId);
            await Assert.That(notifications.All(item =>
                item.Kind == NotificationKind.CheatIncidentDetected
                && item.TargetType == NotificationTargetType.User
                && item.ContentJson.Contains($"cheat-incident:{message.SubmissionId:N}")
                && !item.ContentJson.Contains("flag", StringComparison.OrdinalIgnoreCase)))
                .IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Audience_is_scoped_deduplicated_and_delivery_is_idempotent(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_competition_notifications")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var competitionId = Guid.CreateVersion7();
            var ownerId = Guid.CreateVersion7();
            var managerAndMemberId = Guid.CreateVersion7();
            var observerBotId = Guid.CreateVersion7();
            var approvedMemberId = Guid.CreateVersion7();
            var pendingMemberId = Guid.CreateVersion7();
            var bannedMemberId = Guid.CreateVersion7();
            var approvedTeamId = Guid.CreateVersion7();
            var pendingTeamId = Guid.CreateVersion7();
            var bannedTeamId = Guid.CreateVersion7();

            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                setup.Users.AddRange(
                    Human(ownerId, "notification-owner", UserRole.Organizer, now),
                    Human(managerAndMemberId, "notification-manager", UserRole.Organizer, now),
                    Bot(observerBotId, "notification-relay", now),
                    Human(approvedMemberId, "notification-approved", UserRole.User, now),
                    Human(pendingMemberId, "notification-pending", UserRole.User, now),
                    Human(bannedMemberId, "notification-banned", UserRole.User, now));
                setup.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    OwnerId = ownerId,
                    ManagerIds = [managerAndMemberId],
                    ObserverIds = [observerBotId],
                    Title = "Notification audience",
                    Mode = GameMode.Ctf,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    ConfigurationUpdatedAt = now,
                    FlagDerivationSecret = new byte[32],
                    StartAt = now,
                    EndAt = now.AddHours(1),
                    Status = CompetitionStatus.Running,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.Teams.AddRange(
                    Team(
                        approvedTeamId,
                        competitionId,
                        "Approved",
                        managerAndMemberId,
                        [managerAndMemberId, approvedMemberId],
                        TeamRegistrationStatus.Approved,
                        false,
                        now),
                    Team(
                        pendingTeamId,
                        competitionId,
                        "Pending",
                        pendingMemberId,
                        [pendingMemberId],
                        TeamRegistrationStatus.Pending,
                        false,
                        now),
                    Team(
                        bannedTeamId,
                        competitionId,
                        "Banned",
                        bannedMemberId,
                        [bannedMemberId],
                        TeamRegistrationStatus.Approved,
                        true,
                        now));
                await setup.SaveChangesAsync(ct);
            }

            await using var db = new NoCtfDbContext(options);
            var resolver = new CompetitionNotificationAudienceResolver(db);
            var normalAudience = await resolver.ResolveAsync(competitionId, null, ct);
            var banAudience = await resolver.ResolveAsync(competitionId, bannedTeamId, ct);

            await Assert.That(normalAudience).IsEquivalentTo(
                [ownerId, managerAndMemberId, observerBotId, approvedMemberId]);
            await Assert.That(banAudience).IsEquivalentTo(
                [ownerId, managerAndMemberId, observerBotId, approvedMemberId, bannedMemberId]);
            await Assert.That(normalAudience).DoesNotContain(pendingMemberId);

            var leaderboardAccess = new CompetitionVisibilityAccess(db);
            await Assert.That((await leaderboardAccess.ResolveAsync(
                observerBotId,
                competitionId,
                now,
                ct))?.DataScope).IsEqualTo(LeaderboardDataScope.Live);
            await Assert.That(await leaderboardAccess.ResolveAsync(
                approvedMemberId,
                competitionId,
                now,
                ct)).IsNotNull();

            var delivery = new CompetitionNotificationDelivery(db);
            var payload = new TeamBannedPayload(
                competitionId,
                bannedTeamId,
                "Banned",
                now);
            await delivery.DeliverAsync(
                competitionId,
                bannedTeamId,
                NotificationKind.TeamBanned,
                $"team-banned:{bannedTeamId:N}:1",
                payload,
                bannedTeamId,
                ct);
            await delivery.DeliverAsync(
                competitionId,
                bannedTeamId,
                NotificationKind.TeamBanned,
                $"team-banned:{bannedTeamId:N}:1",
                payload,
                bannedTeamId,
                ct);

            var notifications = await db.Notifications.AsNoTracking()
                .Where(notification => notification.RelatedId == competitionId)
                .ToArrayAsync(ct);
            await Assert.That(notifications).Count().IsEqualTo(1);
            await Assert.That(notifications[0].TargetType)
                .IsEqualTo(NotificationTargetType.TeamMembers);
            await Assert.That(notifications[0].TargetId).IsEqualTo(bannedTeamId);
            await Assert.That(notifications[0].ContentJson)
                .Contains($"team-banned:{bannedTeamId:N}:1");

            await delivery.DeliverAsync(
                competitionId,
                Guid.CreateVersion7(),
                NotificationKind.ChallengePublished,
                $"challenge-published:{competitionId:N}:1",
                new ChallengePublishedPayload(
                    competitionId,
                    Guid.CreateVersion7(),
                    "New challenge",
                    "Web",
                    now.AddSeconds(1)),
                requiredTeamId: null,
                ct);
            var reader = new NotificationReader(db);
            var start = new KeysetNotificationPosition(now.AddDays(-1), Guid.Empty);
            var ownerFeed = await reader.ReadFeedAsync(ownerId, start, 10, ct);
            var approvedMemberFeed = await reader.ReadFeedAsync(
                approvedMemberId,
                start,
                10,
                ct);
            var unrelatedFeed = await reader.ReadFeedAsync(pendingMemberId, start, 10, ct);
            var bannedMemberFeed = await reader.ReadFeedAsync(bannedMemberId, start, 10, ct);

            await Assert.That(ownerFeed).IsEmpty();
            await Assert.That(approvedMemberFeed).Count().IsEqualTo(1);
            await Assert.That(approvedMemberFeed[0].Kind)
                .IsEqualTo(NotificationKind.ChallengePublished);
            await Assert.That(unrelatedFeed).IsEmpty();
            await Assert.That(bannedMemberFeed).Count().IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Hint_delivery_waits_for_publication_and_supersedes_stale_messages(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_hint_notifications")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var competitionId = Guid.CreateVersion7();
            var ownerId = Guid.CreateVersion7();
            var memberId = Guid.CreateVersion7();
            var teamId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var hintId = Guid.CreateVersion7();
            var deletedHintId = Guid.CreateVersion7();
            var futureAt = now.AddHours(1);
            var publishedAt = now.AddMinutes(-1);

            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                setup.Users.AddRange(
                    Human(ownerId, "hint-owner", UserRole.Organizer, now),
                    Human(memberId, "hint-member", UserRole.User, now));
                setup.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    OwnerId = ownerId,
                    Title = "Hint notifications",
                    Mode = GameMode.Ctf,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    ConfigurationUpdatedAt = now,
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(2),
                    Status = CompetitionStatus.Running,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.Teams.Add(Team(
                    teamId,
                    competitionId,
                    "Hint Team",
                    memberId,
                    [memberId],
                    TeamRegistrationStatus.Approved,
                    false,
                    now));
                setup.Challenges.Add(new Challenge
                {
                    Id = challengeId,
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Visibility = ChallengeVisibility.Private,
                    Title = "Paid hint challenge",
                    Direction = "Web",
                    DefinitionJson = """{"schemaVersion":1}""",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    IsPublished = true,
                    Revision = 1,
                    UpdatedAt = now,
                    Hints =
                    [
                        new CompetitionChallengeHint
                        {
                            Id = hintId,
                            Content = "paid secret content",
                            Cost = 25,
                            PublishedAt = futureAt
                        },
                        new CompetitionChallengeHint
                        {
                            Id = deletedHintId,
                            Content = "deleted paid secret",
                            Cost = 50,
                            PublishedAt = publishedAt,
                            HiddenAt = now
                        }
                    ]
                });
                await setup.SaveChangesAsync(ct);
            }

            await using var db = new NoCtfDbContext(options);
            var delivery = new CompetitionNotificationDelivery(db);
            var futureMessage = new PublishHintNotification(
                competitionId,
                competitionChallengeId,
                hintId,
                "Paid hint challenge",
                25,
                futureAt,
                1);
            await CompetitionNotificationMessageHandlers.Handle(
                futureMessage,
                db,
                delivery,
                ct);

            var challenge = await db.CompetitionChallenges
                .SingleAsync(candidate => candidate.Id == competitionChallengeId, ct);
            var hint = challenge.Hints.Single(candidate => candidate.Id == hintId);
            hint.PublishedAt = publishedAt;
            challenge.Revision = 2;
            challenge.UpdatedAt = now.AddSeconds(1);
            await db.SaveChangesAsync(ct);

            await CompetitionNotificationMessageHandlers.Handle(
                futureMessage,
                db,
                delivery,
                ct);
            await CompetitionNotificationMessageHandlers.Handle(
                new PublishHintNotification(
                    competitionId,
                    competitionChallengeId,
                    deletedHintId,
                    "Paid hint challenge",
                    50,
                    publishedAt,
                    1),
                db,
                delivery,
                ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(0);

            var currentMessage = futureMessage with
            {
                PublishedAt = publishedAt,
                PublicationRevision = 2
            };
            await CompetitionNotificationMessageHandlers.Handle(
                currentMessage,
                db,
                delivery,
                ct);
            await CompetitionNotificationMessageHandlers.Handle(
                currentMessage,
                db,
                delivery,
                ct);

            var notifications = await db.Notifications.AsNoTracking()
                .ToArrayAsync(ct);
            await Assert.That(notifications).Count().IsEqualTo(1);
            await Assert.That(notifications.All(notification =>
                notification.Kind == NotificationKind.HintPublished
                && notification.TargetType == NotificationTargetType.CompetitionParticipants
                && notification.TargetId == competitionId
                && notification.ContentJson.Contains($"hint-published:{hintId:N}:2")
                && !notification.ContentJson.Contains("paid secret", StringComparison.Ordinal)))
                .IsTrue();
            var reader = new NotificationReader(db);
            var start = new KeysetNotificationPosition(now.AddDays(-1), Guid.Empty);
            await Assert.That(await reader.ReadFeedAsync(memberId, start, 10, ct))
                .Count().IsEqualTo(1);
            await Assert.That(await reader.ReadFeedAsync(ownerId, start, 10, ct))
                .IsEmpty();
        });
    }

    private static User Human(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static User Bot(Guid id, string userName, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"bot-{id:N}@bot.invalid",
            NormalizedEmail = $"BOT-{id:N}@BOT.INVALID",
            PasswordHash = "test",
            Kind = UserKind.Bot,
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Team Team(
        Guid id,
        Guid competitionId,
        string name,
        Guid captainId,
        Guid[] memberIds,
        TeamRegistrationStatus registrationStatus,
        bool isBanned,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CaptainId = captainId,
            MemberIds = memberIds,
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = registrationStatus,
            RegisteredAt = now,
            IsBanned = isBanned,
            BannedAt = isBanned ? now : null
        };
}
