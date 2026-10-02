using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Notifications.Announcements;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionAnnouncementManagementPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Editing_and_withdrawal_update_inboxes_and_public_readers_without_mutating_history(CancellationToken ct)
    {
        await Run(async (db, fixture) =>
        {
            var manage = new ManageCompetitionAnnouncements(new CompetitionAnnouncementManagementStore(db));
            var inbox = new NotificationReader(db);
            var publicReader = new PublicCompetitionAnnouncementReader(db);
            var initial = await inbox.ListPageAsync(fixture.PlayerId, fixture.CompetitionId, 0, 10, true, NotificationReadScope.Inbox, ct);
            await Assert.That(initial.Total).IsEqualTo(1);
            var changed = await manage.ChangeAsync(new(fixture.CompetitionId, fixture.PublicId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Edit, " New title ", " Updated **body** ", fixture.Now), ct);
            await Assert.That(changed.Failure).IsNull();
            await Assert.That(changed.Announcement!.Audience).IsEqualTo(CompetitionAnnouncementAudience.Participants);
            db.ChangeTracker.Clear();
            await Assert.That((await db.Notifications.AsNoTracking().SingleAsync(item => item.Id == fixture.PublicId, ct)).Title).IsEqualTo("Original title");
            var updated = await inbox.ListPageAsync(fixture.PlayerId, fixture.CompetitionId, 0, 10, true, NotificationReadScope.Inbox, ct);
            var content = (CompetitionAnnouncementNotificationContent)updated.Items.Single().Content;
            await Assert.That(content.Title).IsEqualTo("New title");
            await Assert.That(content.Body).IsEqualTo("Updated **body**");
            await Assert.That(updated.Items.Single().Id).IsEqualTo(fixture.PublicId);
            await Assert.That((await publicReader.ListAsync(fixture.CompetitionId, null, null, 10, ct)).Items!.Single().Title).IsEqualTo("New title");
            await Assert.That((await inbox.ReadThreadAsync(fixture.PlayerId, fixture.PublicId, ct))!.Count).IsEqualTo(1);

            var deleted = await manage.ChangeAsync(new(fixture.CompetitionId, fixture.PublicId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Withdraw, null, null, fixture.Now.AddSeconds(1)), ct);
            await Assert.That(deleted.Announcement!.State).IsEqualTo(CompetitionAnnouncementState.Withdrawn);
            await Assert.That((await inbox.ListPageAsync(fixture.PlayerId, fixture.CompetitionId, 0, 10, true, NotificationReadScope.Inbox, ct)).Total).IsEqualTo(0);
            await Assert.That((await publicReader.ListAsync(fixture.CompetitionId, null, null, 10, ct)).Items!.Count).IsEqualTo(0);
            await Assert.That(await inbox.ReadThreadAsync(fixture.PlayerId, fixture.PublicId, ct)).IsNull();
            var history = await manage.ListAsync(fixture.CompetitionId, true, 0, 10, true, ct);
            await Assert.That(history!.Total).IsEqualTo(2);
            await Assert.That(history.Items.Single(item => item.Id == fixture.PublicId).Body).IsEqualTo("Updated **body**");
            var before = await db.Notifications.CountAsync(ct);
            await manage.ChangeAsync(new(fixture.CompetitionId, fixture.PublicId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Withdraw, null, null, fixture.Now.AddSeconds(2)), ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(before);
            var editingWithdrawn = await manage.ChangeAsync(new(fixture.CompetitionId, fixture.PublicId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Edit, "Forbidden edit", "body", fixture.Now.AddSeconds(3)), ct);
            await Assert.That(editingWithdrawn.Failure).IsEqualTo(CompetitionAnnouncementFailure.Withdrawn);
        }, ct);
    }

    [Test, Timeout(300_000)]
    public async Task Management_scopes_validation_pagination_and_editor_audience_revocation_are_preserved(CancellationToken ct)
    {
        await Run(async (db, fixture) =>
        {
            var manage = new ManageCompetitionAnnouncements(new CompetitionAnnouncementManagementStore(db));
            var invalid = await manage.ChangeAsync(new(fixture.CompetitionId, fixture.PublicId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Edit, " ", "body", fixture.Now), ct);
            await Assert.That(invalid.Failure).IsEqualTo(CompetitionAnnouncementFailure.InvalidContent);
            var wrongScope = await manage.ChangeAsync(new(Guid.NewGuid(), fixture.PublicId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Withdraw, null, null, fixture.Now), ct);
            await Assert.That(wrongScope.Failure).IsEqualTo(CompetitionAnnouncementFailure.NotFound);
            var first = await manage.ListAsync(fixture.CompetitionId, false, 0, 1, false, ct);
            var second = await manage.ListAsync(fixture.CompetitionId, false, 1, 1, false, ct);
            await Assert.That(first!.Total).IsEqualTo(2);
            await Assert.That(first.Items.Single().Id == second!.Items.Single().Id).IsFalse();
            await manage.ChangeAsync(new(fixture.CompetitionId, fixture.StaffId, fixture.EditorId,
                CompetitionAnnouncementChangeAction.Edit, "Staff only", "private", fixture.Now), ct);
            var inbox = new NotificationReader(db);
            await Assert.That(await inbox.ReadThreadAsync(fixture.PlayerId, fixture.StaffId, ct)).IsNull();
            await Assert.That(await inbox.ReadThreadAsync(fixture.EditorId, fixture.StaffId, ct)).IsNotNull();
            var competition = await db.Competitions.SingleAsync(item => item.Id == fixture.CompetitionId, ct);
            competition.ManagerIds = [];
            await db.SaveChangesAsync(ct);
            await Assert.That(await inbox.ReadThreadAsync(fixture.EditorId, fixture.StaffId, ct)).IsNull();
            await Assert.That(await db.Notifications.CountAsync(item => item.Kind == NotificationKind.CompetitionAnnouncement, ct)).IsEqualTo(2);
        }, ct);
    }

    private static Task Run(Func<NoCtfDbContext, Fixture, Task> test, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("announcements").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var fixture = new Fixture(DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var (id, name) in new[] { (fixture.OwnerId, "owner"), (fixture.PlayerId, "player"), (fixture.EditorId, "editor") })
            db.Users.Add(new User { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name+"@test.invalid",
                PasswordHash = "test", AccountStatus = UserAccountStatus.Active, CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
        db.Competitions.Add(new CtfCompetition { Id = fixture.CompetitionId, OwnerId = fixture.OwnerId, ManagerIds = [fixture.EditorId], Title = "Announcement management",
            AccessMode = CompetitionAccessMode.Public, Status = CompetitionStatus.Running, ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32], CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
        db.Teams.Add(new Team { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, Name = "Participants", CaptainId = fixture.PlayerId,
            MemberIds = [fixture.PlayerId], InvitationToken = new string('a',32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now });
        foreach (var (id, target) in new[] { (fixture.PublicId, NotificationTargetType.CompetitionParticipants), (fixture.StaffId, NotificationTargetType.CompetitionCollaborators) })
        {
            db.Notifications.Add(new CompetitionAnnouncementNotification { Id = id, SourceType = NotificationSourceType.User, SourceId = fixture.OwnerId,
                TargetType = target, TargetId = fixture.CompetitionId, CompetitionId = fixture.CompetitionId, RelatedType = EntityReferenceKind.Competition,
                RelatedId = fixture.CompetitionId, Title = "Original title", Body = "Original body", SentAt = fixture.Now.AddMinutes(-10) });
            db.CompetitionEvents.Add(new AnnouncementPublishedEvent { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
                Level = CompetitionEventLevel.Information, Visibility = target == NotificationTargetType.CompetitionParticipants ? CompetitionEventVisibility.Public : CompetitionEventVisibility.Staff,
                SubjectType = EntityReferenceKind.Notification, SubjectId = id, QuestionId = id, OccurredAt = fixture.Now.AddMinutes(-10) });
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        await test(db, fixture);
    });

    private sealed record Fixture(DateTimeOffset Now, Guid OwnerId, Guid PlayerId, Guid EditorId, Guid CompetitionId, Guid PublicId, Guid StaffId);
}
