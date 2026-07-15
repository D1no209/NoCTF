using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Events;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Plugins.QQBot;

namespace NoCTF.Tests;

public sealed class QqBotPluginTests
{
    [Fact]
    public void TemplateRenderer_OnlyEmitsWhitelistedMilkySegments()
    {
        var renderer = new QqBotTemplateRenderer();
        var rendered = renderer.Render(
            QqBotEventType.Announcement,
            "{announcement_title}\n{announcement_content}",
            new Dictionary<string, string?>
            {
                ["announcement_title"] = "通知[CQ:at,qq=all]",
                ["announcement_content"] = "正文"
            },
            mentionAll: true,
            maxLength: 2000);

        using var document = JsonDocument.Parse(rendered.SegmentsJson);
        var segments = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal("mention_all", segments[0].GetProperty("type").GetString());
        Assert.Equal("text", segments[1].GetProperty("type").GetString());
        Assert.Contains("[CQ:at,qq=all]", segments[1].GetProperty("data").GetProperty("text").GetString());
    }

    [Fact]
    public void TemplateRenderer_RejectsUnknownVariablesAndExpressions()
    {
        var renderer = new QqBotTemplateRenderer();
        Assert.Equal("template_variable_not_allowed:flag", Assert.Throws<QqBotTemplateRenderException>(() =>
            renderer.Render(QqBotEventType.Announcement, "{flag}", new Dictionary<string, string?>(), false, 2000)).Code);
        Assert.Equal("template_expression_not_supported", Assert.Throws<QqBotTemplateRenderException>(() =>
            renderer.Render(QqBotEventType.Announcement, "{{user.name}}", new Dictionary<string, string?>(), false, 2000)).Code);
    }

    [Fact]
    public async Task Outbox_AddsEventAndWorkerTaskWithoutSending()
    {
        await using var db = CreateDb();
        var competitionId = Guid.NewGuid();
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Test",
            Status = CompetitionStatus.Running,
            ModeKey = "ctf",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        var outbox = new QqBotNotificationOutbox(db);
        outbox.Add(CompetitionNotification.Create(
            competitionId, CompetitionNotificationTypes.CompetitionStarted, "competition", competitionId, null,
            $"competition.started:{competitionId:N}:v1", new { competition_name = "Test" }));
        Assert.Equal(1, db.ChangeTracker.Entries<QqBotEvent>().Count(entry => entry.State == EntityState.Added));
        Assert.Equal(1, db.ChangeTracker.Entries<BackgroundTaskItem>().Count(entry => entry.State == EntityState.Added));
        await db.SaveChangesAsync();
        Assert.Equal(QqBotEventStatus.Pending, await db.QqBotEvents.IgnoreQueryFilters().Select(item => item.Status).SingleAsync());
        Assert.Equal(QqBotDispatchJobHandler.JobKeyValue, await db.BackgroundTasks.IgnoreQueryFilters().Select(item => item.Type).SingleAsync());
    }

    [Fact]
    public void PluginLoader_DoesNotRequireOptionalQqBotAssembly()
    {
        NoCTF.Application.Plugins.PluginLoader.EnsureBuiltInPluginsPresent(new[]
        {
            "NoCTF.Plugins.CTF.dll", "NoCTF.Plugins.AWD.dll", "NoCTF.Plugins.AWDP.dll",
            "NoCTF.Plugins.KoH.dll", "NoCTF.Plugins.Penetration.dll"
        });
    }

    [Theory]
    [InlineData(false, true, true, "global_disabled")]
    [InlineData(true, false, true, "competition_disabled")]
    [InlineData(true, true, false, "event_disabled")]
    public async Task Dispatcher_RespectsEveryOptInLayer(
        bool globalEnabled,
        bool competitionEnabled,
        bool eventEnabled,
        string expectedCode)
    {
        await using var db = CreateDb();
        var fixture = await SeedDispatchAsync(db, globalEnabled, competitionEnabled, eventEnabled, groupCount: 1);

        await CreateHandler(db).ExecuteAsync(fixture.Task);

        var botEvent = await db.QqBotEvents.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(QqBotEventStatus.Suppressed, botEvent.Status);
        Assert.Equal(expectedCode, botEvent.LastErrorCode);
        Assert.Empty(await db.QqBotDeliveries.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Dispatcher_FansOutToAuthorizedGroupsAndRemainsIdempotent()
    {
        await using var db = CreateDb();
        var fixture = await SeedDispatchAsync(db, true, true, true, groupCount: 2);
        var handler = CreateHandler(db);

        await handler.ExecuteAsync(fixture.Task);
        await handler.ExecuteAsync(fixture.Task);

        var deliveries = await db.QqBotDeliveries.IgnoreQueryFilters().OrderBy(item => item.QqGroupId).ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(QqBotDeliveryStatus.Pending, delivery.Status);
            Assert.Contains("比赛已开始", delivery.RenderedText);
        });
        Assert.Equal(QqBotEventStatus.Expanded, (await db.QqBotEvents.IgnoreQueryFilters().SingleAsync()).Status);
    }

    [Fact]
    public async Task Dispatcher_RejectsGroupsWithoutPlatformAuthorization()
    {
        await using var db = CreateDb();
        var fixture = await SeedDispatchAsync(db, true, true, true, groupCount: 1, groupsAuthorized: false);

        await CreateHandler(db).ExecuteAsync(fixture.Task);

        var botEvent = await db.QqBotEvents.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(QqBotEventStatus.Failed, botEvent.Status);
        Assert.Equal("no_authorized_group", botEvent.LastErrorCode);
        Assert.Empty(await db.QqBotDeliveries.IgnoreQueryFilters().ToListAsync());
    }

    private static QqBotDispatchJobHandler CreateHandler(ApplicationDbContext db)
        => new(db, new QqBotTemplateRenderer(), new ConfigurationBuilder().Build(),
            NullLogger<QqBotDispatchJobHandler>.Instance);

    private static async Task<DispatchFixture> SeedDispatchAsync(
        ApplicationDbContext db,
        bool globalEnabled,
        bool competitionEnabled,
        bool eventEnabled,
        int groupCount,
        bool groupsAuthorized = true)
    {
        var now = DateTime.UtcNow;
        var competitionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Broadcast Test",
            Status = CompetitionStatus.Running,
            ModeKey = "ctf",
            StartTime = now.AddMinutes(-1),
            EndTime = now.AddHours(1)
        });
        db.QqBotGlobalSettings.Add(new QqBotGlobalSettings
        {
            Id = QqBotDefaults.GlobalSettingsId,
            Enabled = globalEnabled,
            UpdatedAt = now
        });
        db.QqBotAgents.Add(new QqBotAgent
        {
            Id = agentId,
            Name = "test-agent",
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionQqBotSettings.Add(new CompetitionQqBotSettings
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Enabled = competitionEnabled,
            AllowMessages = competitionEnabled,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionQqBotEventRules.Add(new CompetitionQqBotEventRule
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            EventType = QqBotEventType.CompetitionStarted,
            Enabled = eventEnabled,
            UpdatedAt = now
        });
        for (var index = 0; index < groupCount; index++)
        {
            var groupId = Guid.NewGuid();
            db.QqBotGroups.Add(new QqBotGroup
            {
                Id = groupId,
                AgentId = agentId,
                GroupId = 1095173403 + index,
                GroupName = $"test-{index}",
                IsPresent = true,
                IsAuthorized = groupsAuthorized,
                LastSeenAt = now,
                UpdatedAt = now
            });
            db.CompetitionQqBotGroupBindings.Add(new CompetitionQqBotGroupBinding
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                AgentId = agentId,
                GroupId = groupId,
                IsDefault = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.QqBotEvents.Add(new QqBotEvent
        {
            Id = eventId,
            CompetitionId = competitionId,
            EventType = QqBotEventType.CompetitionStarted,
            Status = QqBotEventStatus.Pending,
            Source = QqBotDeliverySource.Automatic,
            SubjectType = "competition",
            SubjectId = competitionId,
            IdempotencyKey = $"competition.started:{competitionId:N}:v1",
            PayloadJson = "{}",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        return new DispatchFixture(new BackgroundTaskItem
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Type = QqBotDispatchJobHandler.JobKeyValue,
            PayloadJson = JsonSerializer.Serialize(new { eventId }),
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private sealed record DispatchFixture(BackgroundTaskItem Task);

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"qqbot-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new TenantContext());
    }
}
