using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class NotificationChangeAudienceResolverTests
{
    [Test]
    public async Task Direct_and_administrator_signals_only_target_their_current_users()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var directId = Guid.NewGuid();
        var administratorId = Guid.NewGuid();
        var inactiveAdministratorId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        db.Users.AddRange(
            User(directId, "direct", UserRole.User, UserAccountStatus.Active),
            User(administratorId, "admin", UserRole.Administrator, UserAccountStatus.Active),
            User(inactiveAdministratorId, "inactive", UserRole.Administrator, UserAccountStatus.Disabled),
            User(unrelatedId, "unrelated", UserRole.User, UserAccountStatus.Active));
        await db.SaveChangesAsync();

        var recipients = await new NotificationChangeAudienceResolver(
            new TestDbContextFactory(options)).ResolveAsync(
            [new(NotificationTargetType.User, directId),
                new(NotificationTargetType.PlatformAdministrators, Notification.PlatformAdministratorsTargetId)],
            CancellationToken.None);

        await Assert.That(recipients).IsEquivalentTo([directId, administratorId]);
    }

    [Test]
    public async Task Competition_signals_use_current_collaborators_and_approved_team_members()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var bannedCaptainId = Guid.NewGuid();
        var formerParticipantId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var threadRootId = Guid.NewGuid();
        db.Users.AddRange(
            User(ownerId, "owner", UserRole.Organizer, UserAccountStatus.Active),
            User(managerId, "manager", UserRole.Organizer, UserAccountStatus.Active),
            User(captainId, "captain", UserRole.User, UserAccountStatus.Active),
            User(memberId, "member", UserRole.User, UserAccountStatus.Active),
            User(bannedCaptainId, "banned", UserRole.User, UserAccountStatus.Active),
            User(formerParticipantId, "former", UserRole.User, UserAccountStatus.Active));
        db.Competitions.Add(new CtfCompetition
        {
            Id = competitionId,
            OwnerId = ownerId,
            ManagerIds = [managerId],
            Title = "Notification audience",
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.AddRange(
            new Team
            {
                Id = teamId,
                CompetitionId = competitionId,
                Name = "Approved",
                CaptainId = captainId,
                MemberIds = [captainId, memberId],
                InvitationToken = Guid.NewGuid().ToString("N"),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            },
            new Team
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                Name = "Banned",
                CaptainId = bannedCaptainId,
                MemberIds = [bannedCaptainId],
                InvitationToken = Guid.NewGuid().ToString("N"),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                IsBanned = true,
                RegisteredAt = now
            });
        db.Notifications.Add(new MessageNotification
        {
            Id = threadRootId,
            SourceType = NotificationSourceType.User,
            SourceId = formerParticipantId,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = competitionId,
            SentAt = now
        });
        await db.SaveChangesAsync();

        var recipients = await new NotificationChangeAudienceResolver(
            new TestDbContextFactory(options)).ResolveAsync(
            [new(NotificationTargetType.CompetitionCollaborators, competitionId),
                new(NotificationTargetType.CompetitionParticipants, competitionId)],
            CancellationToken.None);

        await Assert.That(recipients).IsEquivalentTo(
            [ownerId, managerId, captainId, memberId]);

        var threadRecipients = await new NotificationChangeAudienceResolver(
            new TestDbContextFactory(options)).ResolveAsync(
            [new(NotificationTargetType.TeamMembers, teamId, threadRootId)],
            CancellationToken.None);
        await Assert.That(threadRecipients).IsEquivalentTo(
            [captainId, memberId, formerParticipantId]);
    }

    private static User User(Guid id, string name, UserRole role, UserAccountStatus status) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = role,
        AccountStatus = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class TestDbContextFactory(DbContextOptions<NoCtfDbContext> options)
        : IDbContextFactory<NoCtfDbContext>
    {
        public NoCtfDbContext CreateDbContext() => new(options);
    }
}
