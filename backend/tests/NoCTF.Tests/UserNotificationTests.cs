using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Events;
using NoCTF.Application.Notifications;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public sealed class UserNotificationTests
{
    [Fact]
    public async Task Outbox_FansOutToAuthorizedCompetitionParticipantsAndRemainsIdempotent()
    {
        await using var db = CreateDb();
        var competitionId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var pendingMemberId = Guid.NewGuid();
        var approvedTeamId = Guid.NewGuid();
        var pendingTeamId = Guid.NewGuid();
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            OwnerId = ownerId,
            Title = "Notification Test",
            ModeKey = "ctf"
        });
        db.CompetitionCollaborators.Add(new CompetitionCollaborator
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            UserId = collaboratorId
        });
        db.Teams.AddRange(
            new Team
            {
                Id = approvedTeamId,
                CompetitionId = competitionId,
                CaptainId = memberId,
                Name = "Approved",
                InviteToken = Guid.NewGuid().ToString("N"),
                RegistrationStatus = TeamRegistrationStatus.Approved
            },
            new Team
            {
                Id = pendingTeamId,
                CompetitionId = competitionId,
                CaptainId = pendingMemberId,
                Name = "Pending",
                InviteToken = Guid.NewGuid().ToString("N"),
                RegistrationStatus = TeamRegistrationStatus.Pending
            });
        db.TeamMembers.AddRange(
            new TeamMember
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = approvedTeamId,
                UserId = memberId
            },
            new TeamMember
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = pendingTeamId,
                UserId = pendingMemberId
            });
        await db.SaveChangesAsync();

        var notification = CompetitionNotification.Create(
            competitionId,
            CompetitionNotificationTypes.TeamPenalized,
            "team",
            approvedTeamId,
            ownerId,
            $"team.penalized:{competitionId:N}:{approvedTeamId:N}:v1",
            new
            {
                competition_name = "Notification Test",
                team_name = "Approved",
                penalty_type = "封禁",
                penalty_reason = "internal evidence must not be exposed"
            });
        var outbox = new UserNotificationOutbox(db);

        outbox.Add(notification);
        outbox.Add(notification);
        await db.SaveChangesAsync();
        outbox.Add(notification);
        await db.SaveChangesAsync();

        var items = await db.UserNotifications.OrderBy(item => item.UserId).ToListAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal(
            new[] { collaboratorId, memberId, ownerId }.Order(),
            items.Select(item => item.UserId).Order());
        Assert.DoesNotContain(items, item => item.UserId == pendingMemberId);
        Assert.All(items, item =>
        {
            using var data = JsonDocument.Parse(item.DataJson);
            Assert.Equal("Approved", data.RootElement.GetProperty("team_name").GetString());
            Assert.False(data.RootElement.TryGetProperty("penalty_reason", out _));
        });
    }

    [Fact]
    public async Task Service_OnlyReadsAndMutatesTheAuthenticatedUsersNotifications()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.UserNotifications.AddRange(
            Notification(firstId, userId, "first", now),
            Notification(secondId, userId, "second", now.AddMinutes(1)),
            Notification(Guid.NewGuid(), otherUserId, "other", now.AddMinutes(2)));
        await db.SaveChangesAsync();
        var service = new UserNotificationService(db);

        var page = await service.GetAsync(userId, 20);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(secondId, page.Items[0].Id);
        Assert.Equal(2, page.UnreadCount);
        Assert.False(await service.MarkReadAsync(userId, db.UserNotifications.Single(item => item.UserId == otherUserId).Id));
        Assert.True(await service.MarkReadAsync(userId, firstId));
        Assert.Equal(1, await service.MarkAllReadAsync(userId));
        Assert.All(db.UserNotifications.Where(item => item.UserId == userId), item => Assert.True(item.IsRead));
        Assert.False(db.UserNotifications.Single(item => item.UserId == otherUserId).IsRead);
    }

    [Fact]
    public async Task Outbox_BoundsPublicPayloadsBeforeTheyReachTheNotificationColumn()
    {
        await using var db = CreateDb();
        var competitionId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            OwnerId = ownerId,
            Title = "Payload Test",
            ModeKey = "ctf"
        });
        await db.SaveChangesAsync();
        var outbox = new UserNotificationOutbox(db);

        outbox.Add(CompetitionNotification.Create(
            competitionId,
            CompetitionNotificationTypes.HintPublished,
            "challenge",
            Guid.NewGuid(),
            null,
            "oversized-hint:v1",
            new
            {
                problem_title = new string('P', 1000),
                hint_title = new string('T', 1000),
                hint_content = string.Concat(Enumerable.Repeat("\0😀", 5000))
            }));
        await db.SaveChangesAsync();

        var item = await db.UserNotifications.SingleAsync();
        using var data = JsonDocument.Parse(item.DataJson);
        Assert.True(item.DataJson.Length <= 4096);
        Assert.Equal(256, data.RootElement.GetProperty("problem_title").GetString()!.Length);
        Assert.Equal(256, data.RootElement.GetProperty("hint_title").GetString()!.Length);
        var hintContent = data.RootElement.GetProperty("hint_content").GetString()!;
        Assert.True(hintContent.Length <= 1800);
        Assert.DoesNotContain('\uFFFD', hintContent);
    }

    [Fact]
    public void CompositeOutbox_IsolatesSinkFailures()
    {
        var collecting = new CollectingSink();
        var outbox = new CompositeCompetitionNotificationOutbox(
            new ICompetitionNotificationSink[] { new ThrowingSink(), collecting },
            NullLogger<CompositeCompetitionNotificationOutbox>.Instance);
        var notification = CompetitionNotification.Create(
            Guid.NewGuid(),
            CompetitionNotificationTypes.CompetitionStarted,
            "competition",
            null,
            null,
            "event:v1",
            new { competition_name = "Test" });

        outbox.Add(notification);

        Assert.Same(notification, collecting.Notification);
    }

    private static UserNotification Notification(Guid id, Guid userId, string key, DateTime createdAt)
        => new()
        {
            Id = id,
            UserId = userId,
            Type = CompetitionNotificationTypes.Announcement,
            DataJson = "{}",
            IdempotencyKey = key,
            CreatedAt = createdAt
        };

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notifications-{Guid.NewGuid():N}")
            .UseSharedInMemoryServiceProvider()
            .Options;
        return new ApplicationDbContext(options, new TenantContext());
    }

    private sealed class ThrowingSink : ICompetitionNotificationSink
    {
        public void Add(CompetitionNotification notification)
            => throw new InvalidOperationException("expected test failure");
    }

    private sealed class CollectingSink : ICompetitionNotificationSink
    {
        public CompetitionNotification? Notification { get; private set; }

        public void Add(CompetitionNotification notification)
            => Notification = notification;
    }
}
