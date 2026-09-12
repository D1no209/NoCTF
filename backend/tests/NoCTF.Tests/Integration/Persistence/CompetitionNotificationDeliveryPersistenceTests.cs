using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Permissions;
using NoCTF.Infrastructure.Competitions.Events;
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
    public async Task Announcement_writes_notification_and_permanent_event_without_copying_body(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_announcement_event")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var competitionId = Guid.CreateVersion7();
            var ownerId = Guid.CreateVersion7();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            db.Users.Add(Human(ownerId, "announcement-owner", UserRole.Organizer, now));
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Announcement competition",
                Mode = GameMode.Ctf,
                ConfigurationJson = "{}",
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1),
                EndAt = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);

            var outbox = new RecordingOutbox();
            var delivery = new CompetitionNotificationDelivery(
                db,
                new CompetitionEventStore(db, outbox));
            var published = await delivery.PublishAsync(new(
                competitionId,
                ownerId,
                "比赛环境维护通知",
                "本段完整公告正文只保存在通知内容中。",
                CompetitionAnnouncementAudience.Participants,
                now), ct);

            await Assert.That(published).IsNotNull();
            var notification = await db.Notifications.AsNoTracking().SingleAsync(ct);
            await Assert.That(notification.ContentJson).Contains("完整公告正文");
            var permanentEvent = await db.CompetitionEvents.AsNoTracking().SingleAsync(ct);
            await Assert.That(permanentEvent.Kind)
                .IsEqualTo(CompetitionEventKind.AnnouncementPublished);
            await Assert.That(permanentEvent.Visibility)
                .IsEqualTo(CompetitionEventVisibility.Public);
            await Assert.That(permanentEvent.SubjectType)
                .IsEqualTo(EntityReferenceKind.Notification);
            await Assert.That(permanentEvent.SubjectId).IsEqualTo(notification.Id);
            await Assert.That(permanentEvent.PayloadJson)
                .DoesNotContain("完整公告正文");
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Foreign_flag_detection_notifies_only_evidence_readers_and_is_idempotent(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
            await db.Database.EnsureCreatedAsync(ct);
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
                ConfigurationJson = "{}",
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
                && item.ContentJson.Contains($"cheat-incident:{message.GameplayFactId:N}")
                && !item.ContentJson.Contains("flag", StringComparison.OrdinalIgnoreCase)))
                .IsTrue();

            var appeal = new TeamBanAppealSubmitted(
                competitionId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "Appealing team",
                now.AddSeconds(1));
            await CompetitionNotificationMessageHandlers.Handle(appeal, db, delivery, ct);
            await CompetitionNotificationMessageHandlers.Handle(appeal, db, delivery, ct);
            var appealNotifications = await db.Notifications.AsNoTracking()
                .Where(item => item.Kind == NotificationKind.TeamBanAppealSubmitted)
                .ToArrayAsync(ct);
            await Assert.That(appealNotifications).Count().IsEqualTo(4);
            await Assert.That(appealNotifications.Select(item => item.TargetId)).IsEquivalentTo([
                administratorId,
                ownerId,
                managerId,
                judgeId
            ]);
            await Assert.That(appealNotifications.All(item =>
                item.ContentJson.Contains($"team-ban-appeal:{appeal.AppealEventId:N}")))
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
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
                await setup.Database.EnsureCreatedAsync(ct);
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

            await db.Competitions
                .Where(competition => competition.Id == competitionId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        competition => competition.AccessMode,
                        CompetitionAccessMode.StaffOnly),
                    ct);
            var hiddenAudience = await resolver.ResolveAsync(
                competitionId,
                bannedTeamId,
                ct);
            await Assert.That(hiddenAudience).IsEquivalentTo(
                [ownerId, managerAndMemberId, observerBotId]);
            await Assert.That(await leaderboardAccess.ResolveAsync(
                approvedMemberId,
                competitionId,
                now,
                ct)).IsNull();
            await Assert.That((await leaderboardAccess.ResolveAsync(
                observerBotId,
                competitionId,
                now,
                ct))?.DataScope).IsEqualTo(LeaderboardDataScope.Live);
            await db.Competitions
                .Where(competition => competition.Id == competitionId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        competition => competition.AccessMode,
                        CompetitionAccessMode.Public),
                    ct);

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

            var announcedBan = new TeamBanned(
                competitionId,
                bannedTeamId,
                "Banned",
                now.AddMilliseconds(1),
                TeamBanAnnouncementKind.ConfirmedCheating);
            var banEventStore = new CompetitionEventStore(db, new RecordingOutbox());
            await CompetitionNotificationMessageHandlers.Handle(
                announcedBan,
                db,
                delivery,
                ct,
                banEventStore);
            await CompetitionNotificationMessageHandlers.Handle(
                announcedBan,
                db,
                delivery,
                ct,
                banEventStore);

            var announcements = await db.Notifications.AsNoTracking()
                .Where(notification =>
                    notification.RelatedId == competitionId
                    && notification.Kind == NotificationKind.CompetitionAnnouncement)
                .ToArrayAsync(ct);
            await Assert.That(announcements).Count().IsEqualTo(1);
            await Assert.That(announcements[0].TargetType)
                .IsEqualTo(NotificationTargetType.CompetitionParticipants);
            await Assert.That(announcements[0].ContentJson).Contains("赛事纪律公告");
            await Assert.That(announcements[0].ContentJson).Contains("作弊行为");
            await Assert.That(announcements[0].ContentJson).Contains("Banned");

            var publicBanEvents = await db.CompetitionEvents.AsNoTracking()
                .Where(item =>
                    item.CompetitionId == competitionId
                    && item.Kind == CompetitionEventKind.TeamBanned
                    && item.Visibility == CompetitionEventVisibility.Public
                    && item.SubjectType == EntityReferenceKind.Team
                    && item.SubjectId == bannedTeamId)
                .ToArrayAsync(ct);
            await Assert.That(publicBanEvents).Count().IsEqualTo(1);

            var internalTracks = new CompetitionTrackConfiguration(
                CompetitionTrackConfiguration.CurrentSchemaVersion,
                [
                    CompetitionTrackConfiguration.DefaultFor(GameMode.Ctf).DefaultTrack,
                    new CompetitionTrackDefinition(
                        "internal",
                        "Internal",
                        IsDefault: false,
                        IsPublicSelectable: false,
                        IsInternal: true,
                        EarnsScore: false,
                        EarnsBlood: false,
                        AffectsDynamicChallengeScore: false,
                        VisibleOnLeaderboard: false,
                        AffectsCompetitiveResults: false)
                ]);
            await db.Competitions.Where(item => item.Id == competitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.TracksEnabled, true)
                    .SetProperty(
                        item => item.TrackConfigurationJson,
                        CompetitionTrackConfiguration.Serialize(internalTracks)), ct);
            await db.Teams.Where(team => team.Id == bannedTeamId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    team => team.TrackKey,
                    "internal"), ct);

            await CompetitionNotificationMessageHandlers.Handle(
                announcedBan with { BannedAt = now.AddMilliseconds(2) },
                db,
                delivery,
                ct,
                banEventStore);

            await Assert.That(await db.Notifications.AsNoTracking().CountAsync(notification =>
                notification.RelatedId == competitionId
                && notification.Kind == NotificationKind.CompetitionAnnouncement, ct))
                .IsEqualTo(1);
            await Assert.That(await db.Notifications.AsNoTracking().CountAsync(notification =>
                notification.TargetType == NotificationTargetType.TeamMembers
                && notification.TargetId == bannedTeamId, ct))
                .IsEqualTo(3);
            await Assert.That(await db.CompetitionEvents.AsNoTracking().CountAsync(item =>
                item.CompetitionId == competitionId
                && item.Kind == CompetitionEventKind.TeamBanned
                && item.Visibility == CompetitionEventVisibility.Public, ct))
                .IsEqualTo(1);

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
            await Assert.That(approvedMemberFeed.Select(notification => notification.Kind))
                .IsEquivalentTo([
                    NotificationKind.CompetitionAnnouncement,
                    NotificationKind.ChallengePublished
                ]);
            await Assert.That(unrelatedFeed).IsEmpty();
            await Assert.That(bannedMemberFeed).Count().IsEqualTo(3);
            await Assert.That(bannedMemberFeed.All(
                notification => notification.Kind == NotificationKind.TeamBanned)).IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Hint_delivery_waits_for_publication_and_supersedes_stale_messages(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
                await setup.Database.EnsureCreatedAsync(ct);
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
                    RulesJson = "{}",
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
                futureAt);
            await CompetitionNotificationMessageHandlers.Handle(
                futureMessage,
                db,
                delivery,
                TimeProvider.System,
                ct);

            var challenge = await db.CompetitionChallenges
                .SingleAsync(candidate => candidate.Id == competitionChallengeId, ct);
            var hint = challenge.Hints.Single(candidate => candidate.Id == hintId);
            hint.PublishedAt = publishedAt;
            challenge.UpdatedAt = now.AddSeconds(1);
            await db.SaveChangesAsync(ct);

            await CompetitionNotificationMessageHandlers.Handle(
                futureMessage,
                db,
                delivery,
                TimeProvider.System,
                ct);
            await CompetitionNotificationMessageHandlers.Handle(
                new PublishHintNotification(
                    competitionId,
                    competitionChallengeId,
                    deletedHintId,
                    "Paid hint challenge",
                    50,
                    publishedAt),
                db,
                delivery,
                TimeProvider.System,
                ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(0);

            var currentMessage = futureMessage with
            {
                PublishedAt = publishedAt,
            };
            await CompetitionNotificationMessageHandlers.Handle(
                currentMessage,
                db,
                delivery,
                TimeProvider.System,
                ct);
            await CompetitionNotificationMessageHandlers.Handle(
                currentMessage,
                db,
                delivery,
                TimeProvider.System,
                ct);

            var notifications = await db.Notifications.AsNoTracking()
                .ToArrayAsync(ct);
            await Assert.That(notifications).Count().IsEqualTo(1);
            var notification = notifications.Single();
            var payload = JsonSerializer.Deserialize<JsonElement>(notification.ContentJson);
            await Assert.That(notification.Kind).IsEqualTo(NotificationKind.HintPublished);
            await Assert.That(notification.TargetType)
                .IsEqualTo(NotificationTargetType.CompetitionParticipants);
            await Assert.That(notification.TargetId).IsEqualTo(competitionId);
            await Assert.That(payload.GetProperty("sourceEventKey").GetString())
                .IsEqualTo($"hint-published:{hintId:N}:{publishedAt.UtcTicks}");
            await Assert.That(payload.GetProperty("hintId").GetGuid()).IsEqualTo(hintId);
            await Assert.That(payload.GetProperty("publishedAt").GetDateTimeOffset())
                .IsEqualTo(publishedAt);
            await Assert.That(notification.ContentJson.Contains(
                "paid secret",
                StringComparison.Ordinal)).IsFalse();
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
            CaptainId = captainId,
            MemberIds = memberIds,
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = registrationStatus,
            RegisteredAt = now,
            IsBanned = isBanned,
            BannedAt = isBanned ? now : null
        };

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
