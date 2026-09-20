using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("Notifications")]
[NotInParallel]
public sealed class PublicCompetitionAnnouncementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Reader_ExposesOnlyPublicParticipantAnnouncements(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_public_announcements")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
            var ownerId = Guid.CreateVersion7(now);
            var publicId = Guid.CreateVersion7(now.AddMilliseconds(1));
            var draftId = Guid.CreateVersion7(now.AddMilliseconds(2));
            var staffId = Guid.CreateVersion7(now.AddMilliseconds(3));

            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "announcement-owner",
                    NormalizedUserName = "ANNOUNCEMENT-OWNER",
                    Email = "announcement-owner@example.test",
                    PasswordHash = "test",
                    Kind = UserKind.Human,
                    Role = UserRole.Organizer,
                    AccountStatus = UserAccountStatus.Active,
                    EmailVerifiedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Competitions.AddRange(
                    Competition(publicId, ownerId, "Public", CompetitionStatus.Running, CompetitionAccessMode.Public, now),
                    Competition(draftId, ownerId, "Draft", CompetitionStatus.Draft, CompetitionAccessMode.Public, now),
                    Competition(staffId, ownerId, "Staff", CompetitionStatus.Running, CompetitionAccessMode.StaffOnly, now));
                AddAnnouncement(seed, publicId, ownerId, now.AddSeconds(1), participant: true, publicEvent: true, "Public title", "Public body");
                AddAnnouncement(seed, publicId, ownerId, now.AddSeconds(2), participant: false, publicEvent: false, "Staff title", "Staff body");
                AddAnnouncement(seed, publicId, ownerId, now.AddSeconds(3), participant: true, publicEvent: false, "Unpublished", "Hidden body");
                AddAnnouncement(seed, draftId, ownerId, now.AddSeconds(4), participant: true, publicEvent: true, "Draft title", "Draft body");
                AddAnnouncement(seed, staffId, ownerId, now.AddSeconds(5), participant: true, publicEvent: true, "Staff competition", "Staff competition body");
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var verify = new NoCtfDbContext(options);
            var reader = new PublicCompetitionAnnouncementReader(verify);
            var available = await reader.ListAsync(publicId, null, null, 50, cancellationToken);
            var draft = await reader.ListAsync(draftId, null, null, 50, cancellationToken);
            var staff = await reader.ListAsync(staffId, null, null, 50, cancellationToken);

            await Assert.That(available.State)
                .IsEqualTo(PublicCompetitionAnnouncementReadState.Available);
            await Assert.That(available.Items).IsNotNull();
            await Assert.That(available.Items!).HasSingleItem();
            await Assert.That(available.Items![0].Title).IsEqualTo("Public title");
            await Assert.That(available.Items![0].Body).IsEqualTo("Public body");
            await Assert.That(draft.State)
                .IsEqualTo(PublicCompetitionAnnouncementReadState.CompetitionNotFound);
            await Assert.That(staff.State)
                .IsEqualTo(PublicCompetitionAnnouncementReadState.CompetitionNotFound);
        });
    }

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        CompetitionStatus status,
        CompetitionAccessMode accessMode,
        DateTimeOffset now) => new()
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = status,
            AccessMode = accessMode,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static void AddAnnouncement(
        NoCtfDbContext db,
        Guid competitionId,
        Guid ownerId,
        DateTimeOffset sentAt,
        bool participant,
        bool publicEvent,
        string title,
        string body)
    {
        var notificationId = Guid.CreateVersion7(sentAt);
        db.Notifications.Add(new Notification
        {
            Id = notificationId,
            SourceType = NotificationSourceType.User,
            SourceId = ownerId,
            TargetType = participant
                ? NotificationTargetType.CompetitionParticipants
                : NotificationTargetType.CompetitionCollaborators,
            TargetId = competitionId,
            Kind = NotificationKind.CompetitionAnnouncement,
            ContentJson = $$"""{"schemaVersion":1,"title":"{{title}}","body":"{{body}}"}""",
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = competitionId,
            SentAt = sentAt
        });
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = Guid.CreateVersion7(sentAt.AddTicks(1)),
            CompetitionId = competitionId,
            Kind = CompetitionEventKind.AnnouncementPublished,
            Level = CompetitionEventLevel.Information,
            Visibility = publicEvent
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            SubjectType = EntityReferenceKind.Notification,
            SubjectId = notificationId,
            PayloadJson = $$"""{"schemaVersion":1,"questionId":"{{notificationId}}"}""",
            OccurredAt = sentAt
        });
    }
}
