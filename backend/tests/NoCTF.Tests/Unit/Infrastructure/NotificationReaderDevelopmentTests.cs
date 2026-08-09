using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class NotificationReaderDevelopmentTests
{
    [Test]
    public async Task InMemory_development_provider_preserves_competition_visibility_and_threads()
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
        var ownerId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddMilliseconds(1));
        var rootId = Guid.CreateVersion7(now.AddMilliseconds(2));
        var replyId = Guid.CreateVersion7(now.AddMilliseconds(3));

        await using var db = new NoCtfDbContext(options);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "development-owner",
            NormalizedUserName = "DEVELOPMENT-OWNER",
            Email = "development-owner@example.test",
            NormalizedEmail = "DEVELOPMENT-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Development notification reader",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Notifications.AddRange(
            new Notification
            {
                Id = rootId,
                SourceType = NotificationSourceType.User,
                SourceId = ownerId,
                TargetType = NotificationTargetType.CompetitionCollaborators,
                TargetId = competitionId,
                Kind = NotificationKind.CompetitionAnnouncement,
                ContentJson = """{"schemaVersion":1,"title":"announcement"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = competitionId,
                SentAt = now
            },
            new Notification
            {
                Id = replyId,
                SourceType = NotificationSourceType.System,
                TargetType = NotificationTargetType.TeamMembers,
                TargetId = Guid.CreateVersion7(now.AddMilliseconds(4)),
                Kind = NotificationKind.Message,
                ContentJson = """{"schemaVersion":1,"body":"follow-up"}""",
                RelatedType = EntityReferenceKind.Competition,
                RelatedId = competitionId,
                ReplyToId = rootId,
                SentAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync();

        var reader = new NotificationReader(db);
        var list = await reader.ListCompetitionAsync(
            ownerId,
            competitionId,
            null,
            null,
            10,
            CancellationToken.None);
        var thread = await reader.ReadThreadAsync(ownerId, rootId, CancellationToken.None);

        await Assert.That(list.Select(item => item.Id))
            .IsEquivalentTo([rootId, replyId]);
        await Assert.That(list.Single(item => item.Id == rootId).SourceDisplayName)
            .IsEqualTo("development-owner");
        await Assert.That(thread).IsNotNull();
        await Assert.That(thread!.Select(item => item.Id))
            .IsEquivalentTo([rootId, replyId]);
    }
}
