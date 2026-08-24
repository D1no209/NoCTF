using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("Notifications")]
[NotInParallel]
public sealed class NotificationReaderPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Dynamic_audiences_and_participant_threads_remain_keyset_stable(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_notification_reader")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
            var ids = new TestIds(now);

            await SeedAsync(options, ids, now, cancellationToken);

            await using (var beforeChange = new NoCtfDbContext(options))
            {
                var reader = new NotificationReader(beforeChange);
                await Assert.That(await reader.ReadThreadAsync(
                    ids.FormerManagerId,
                    ids.RootId,
                    cancellationToken)).Count().IsEqualTo(3);
                await Assert.That(await reader.ReadThreadAsync(
                    ids.FormerManagerId,
                    ids.OwnerReplyId,
                    cancellationToken)).Count().IsEqualTo(3);
                await Assert.That((await reader.ListAsync(
                    ids.FormerParticipantId,
                    null,
                    null,
                    10,
                    cancellationToken)).Select(item => item.Id))
                    .Contains(ids.ParticipantAnnouncementId);
                var formerParticipantInbox = await reader.ListAsync(
                    ids.FormerParticipantId,
                    null,
                    null,
                    10,
                    NotificationReadScope.Inbox,
                    cancellationToken);
                await Assert.That(formerParticipantInbox.Select(item => item.Id))
                    .IsEquivalentTo([ids.ManualAnnouncementId]);
                var competitionNotifications = await reader.ListCompetitionAsync(
                    ids.FormerManagerId,
                    ids.CompetitionId,
                    null,
                    null,
                    10,
                    cancellationToken);
                await Assert.That(competitionNotifications.Select(item => item.Id))
                    .IsEquivalentTo([ids.RootId, ids.FormerReplyId, ids.OwnerReplyId]);
                await Assert.That(competitionNotifications.Single(
                    item => item.Id == ids.RootId).SourceDisplayName)
                    .IsEqualTo("notification-asker");
                await Assert.That(await reader.ListAsync(
                    ids.NewParticipantId,
                    null,
                    null,
                    10,
                    cancellationToken)).IsEmpty();
            }

            await using (var mutate = new NoCtfDbContext(options))
            {
                var competition = await mutate.Competitions.SingleAsync(
                    candidate => candidate.Id == ids.CompetitionId,
                    cancellationToken);
                competition.ManagerIds = [ids.CurrentManagerId];
                var team = await mutate.Teams.SingleAsync(
                    candidate => candidate.Id == ids.TeamId,
                    cancellationToken);
                team.MemberIds = [ids.CaptainId, ids.NewParticipantId];
                await mutate.SaveChangesAsync(cancellationToken);
            }

            KeysetNotificationPosition checkpoint;
            NotificationView[] firstPage;
            await using (var afterChange = new NoCtfDbContext(options))
            {
                var reader = new NotificationReader(afterChange);
                var formerManagerThread = await reader.ReadThreadAsync(
                    ids.FormerManagerId,
                    ids.RootId,
                    cancellationToken);
                await Assert.That(formerManagerThread).IsNotNull();
                await Assert.That(formerManagerThread!.Select(item => item.Id))
                    .IsEquivalentTo([ids.RootId, ids.FormerReplyId, ids.OwnerReplyId]);
                await Assert.That(await reader.ReadThreadAsync(
                    ids.CurrentManagerId,
                    ids.RootId,
                    cancellationToken)).Count().IsEqualTo(3);
                await Assert.That(await reader.ReadThreadAsync(
                    ids.AskerId,
                    ids.RootId,
                    cancellationToken)).Count().IsEqualTo(3);
                await Assert.That(await reader.ReadThreadAsync(
                    ids.UnrelatedId,
                    ids.RootId,
                    cancellationToken)).IsNull();

                await Assert.That(await reader.ListAsync(
                    ids.FormerParticipantId,
                    null,
                    null,
                    10,
                    cancellationToken)).IsEmpty();
                await Assert.That((await reader.ListAsync(
                    ids.NewParticipantId,
                    null,
                    null,
                    10,
                    cancellationToken)).Select(item => item.Id))
                    .Contains(ids.ParticipantAnnouncementId);
                var newParticipantInbox = await reader.ListAsync(
                    ids.NewParticipantId,
                    null,
                    null,
                    10,
                    NotificationReadScope.Inbox,
                    cancellationToken);
                await Assert.That(newParticipantInbox.Select(item => item.Id))
                    .IsEquivalentTo([ids.ManualAnnouncementId]);

                checkpoint = await reader.GetFeedCheckpointAsync(
                    ids.FormerManagerId,
                    now.AddMinutes(1),
                    cancellationToken);
                firstPage = (await reader.ListAsync(
                    ids.FormerManagerId,
                    null,
                    null,
                    2,
                    cancellationToken)).ToArray();
                await Assert.That(firstPage.Select(item => item.Id))
                    .IsEquivalentTo([ids.OwnerReplyId, ids.FormerReplyId]);
            }

            await using (var append = new NoCtfDbContext(options))
            {
                append.Notifications.Add(new Notification
                {
                    Id = ids.CurrentReplyId,
                    SourceType = NotificationSourceType.User,
                    SourceId = ids.CurrentManagerId,
                    TargetType = NotificationTargetType.CompetitionCollaborators,
                    TargetId = ids.CompetitionId,
                    Kind = NotificationKind.Message,
                    ContentJson = """{"schemaVersion":1,"body":"current handler reply"}""",
                    RelatedType = EntityReferenceKind.Competition,
                    RelatedId = ids.CompetitionId,
                    ThreadRootId = ids.RootId,
                    ReplyToId = ids.RootId,
                    SentAt = now.AddSeconds(4)
                });
                await append.SaveChangesAsync(cancellationToken);
            }

            await using (var afterAppend = new NoCtfDbContext(options))
            {
                var reader = new NotificationReader(afterAppend);
                var feed = await reader.ReadFeedAsync(
                    ids.FormerManagerId,
                    checkpoint,
                    10,
                    cancellationToken);
                await Assert.That(feed.Select(item => item.Id))
                    .IsEquivalentTo([ids.CurrentReplyId]);

                var secondPage = await reader.ListAsync(
                    ids.FormerManagerId,
                    firstPage[^1].SentAt,
                    firstPage[^1].Id,
                    2,
                    cancellationToken);
                await Assert.That(secondPage.Select(item => item.Id))
                    .IsEquivalentTo([ids.RootId]);
                await Assert.That(firstPage.Concat(secondPage).Select(item => item.Id))
                    .IsEquivalentTo([ids.RootId, ids.FormerReplyId, ids.OwnerReplyId]);
                await Assert.That(firstPage.Concat(secondPage).Select(item => item.Id).Distinct())
                    .Count().IsEqualTo(3);

                var refreshed = await reader.ListAsync(
                    ids.FormerManagerId,
                    null,
                    null,
                    10,
                    cancellationToken);
                await Assert.That(refreshed.Select(item => item.Id)).IsEquivalentTo([
                    ids.RootId,
                    ids.FormerReplyId,
                    ids.OwnerReplyId,
                    ids.CurrentReplyId
                ]);
            }
        });
    }

    private static async Task SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        TestIds ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.AddRange(
            User(ids.OwnerId, "notification-owner", UserRole.Organizer, now),
            User(ids.FormerManagerId, "notification-former-manager", UserRole.Organizer, now),
            User(ids.CurrentManagerId, "notification-current-manager", UserRole.Organizer, now),
            User(ids.AskerId, "notification-asker", UserRole.User, now),
            User(ids.CaptainId, "notification-captain", UserRole.User, now),
            User(ids.FormerParticipantId, "notification-former-participant", UserRole.User, now),
            User(ids.NewParticipantId, "notification-new-participant", UserRole.User, now),
            User(ids.UnrelatedId, "notification-unrelated", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = ids.CompetitionId,
            OwnerId = ids.OwnerId,
            ManagerIds = [ids.FormerManagerId],
            Title = "Notification reader",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = ids.TeamId,
            CompetitionId = ids.CompetitionId,
            Name = "Notification team",
            CaptainId = ids.CaptainId,
            MemberIds = [ids.CaptainId, ids.FormerParticipantId],
            InvitationToken = ids.TeamId.ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Notifications.AddRange(
            new Notification
            {
                Id = ids.RootId,
                SourceType = NotificationSourceType.User,
                SourceId = ids.AskerId,
                TargetType = NotificationTargetType.CompetitionCollaborators,
                TargetId = ids.CompetitionId,
                Kind = NotificationKind.QuestionOpened,
                ContentJson = """{"schemaVersion":1,"title":"private question"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = ids.CompetitionId,
                SentAt = now.AddSeconds(1)
            },
            new Notification
            {
                Id = ids.FormerReplyId,
                SourceType = NotificationSourceType.User,
                SourceId = ids.FormerManagerId,
                TargetType = NotificationTargetType.CompetitionCollaborators,
                TargetId = ids.CompetitionId,
                Kind = NotificationKind.Message,
                ContentJson = """{"schemaVersion":1,"body":"former handler reply"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = ids.CompetitionId,
                ThreadRootId = ids.RootId,
                ReplyToId = ids.RootId,
                SentAt = now.AddSeconds(2)
            },
            new Notification
            {
                Id = ids.OwnerReplyId,
                SourceType = NotificationSourceType.User,
                SourceId = ids.OwnerId,
                TargetType = NotificationTargetType.CompetitionCollaborators,
                TargetId = ids.CompetitionId,
                Kind = NotificationKind.Message,
                ContentJson = """{"schemaVersion":1,"body":"owner reply"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = ids.CompetitionId,
                ThreadRootId = ids.RootId,
                ReplyToId = ids.RootId,
                SentAt = now.AddSeconds(3)
            },
            new Notification
            {
                Id = ids.ParticipantAnnouncementId,
                SourceType = NotificationSourceType.System,
                TargetType = NotificationTargetType.CompetitionParticipants,
                TargetId = ids.CompetitionId,
                Kind = NotificationKind.CompetitionAnnouncement,
                ContentJson = """{"schemaVersion":1,"body":"participant notice"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = ids.CompetitionId,
                SentAt = now.AddMilliseconds(500)
            },
            new Notification
            {
                Id = ids.ManualAnnouncementId,
                SourceType = NotificationSourceType.User,
                SourceId = ids.OwnerId,
                TargetType = NotificationTargetType.CompetitionParticipants,
                TargetId = ids.CompetitionId,
                Kind = NotificationKind.CompetitionAnnouncement,
                ContentJson = """{"schemaVersion":1,"body":"manual official notice"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = ids.CompetitionId,
                SentAt = now.AddMilliseconds(600)
            });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static User User(
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
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private sealed class TestIds(DateTimeOffset now)
    {
        public Guid CompetitionId { get; } = Guid.CreateVersion7(now);
        public Guid TeamId { get; } = Guid.CreateVersion7(now.AddMilliseconds(1));
        public Guid OwnerId { get; } = Guid.CreateVersion7(now.AddMilliseconds(2));
        public Guid FormerManagerId { get; } = Guid.CreateVersion7(now.AddMilliseconds(3));
        public Guid CurrentManagerId { get; } = Guid.CreateVersion7(now.AddMilliseconds(4));
        public Guid AskerId { get; } = Guid.CreateVersion7(now.AddMilliseconds(5));
        public Guid CaptainId { get; } = Guid.CreateVersion7(now.AddMilliseconds(6));
        public Guid FormerParticipantId { get; } = Guid.CreateVersion7(now.AddMilliseconds(7));
        public Guid NewParticipantId { get; } = Guid.CreateVersion7(now.AddMilliseconds(8));
        public Guid UnrelatedId { get; } = Guid.CreateVersion7(now.AddMilliseconds(9));
        public Guid ParticipantAnnouncementId { get; } = Guid.CreateVersion7(now.AddMilliseconds(500));
        public Guid ManualAnnouncementId { get; } = Guid.CreateVersion7(now.AddMilliseconds(600));
        public Guid RootId { get; } = Guid.CreateVersion7(now.AddSeconds(1));
        public Guid FormerReplyId { get; } = Guid.CreateVersion7(now.AddSeconds(2));
        public Guid OwnerReplyId { get; } = Guid.CreateVersion7(now.AddSeconds(3));
        public Guid CurrentReplyId { get; } = Guid.CreateVersion7(now.AddSeconds(4));
    }
}
