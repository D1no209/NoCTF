using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Events;
using NoCTF.Application.QqBot;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.QQBot;

internal sealed class QqBotAdministrationService(
    ApplicationDbContext db,
    ICompetitionNotificationOutbox outbox,
    QqBotTemplateRenderer renderer) : IQqBotAdministrationService
{
    private static readonly QqBotEventType[] EventTypes = Enum.GetValues<QqBotEventType>();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsAvailable => true;

    public async Task<QqBotAdminOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateGlobalSettingsAsync(ct);
        var agents = await db.QqBotAgents.AsNoTracking().OrderBy(item => item.Name).ToListAsync(ct);
        var groups = await db.QqBotGroups.AsNoTracking()
            .OrderBy(item => item.AgentId).ThenBy(item => item.GroupId).ToListAsync(ct);
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var statistics = await db.QqBotDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.UpdatedAt >= cutoff)
            .GroupBy(item => item.AgentId)
            .Select(group => new AgentStatistics(
                group.Key,
                group.Count(item => item.Status == QqBotDeliveryStatus.Pending || item.Status == QqBotDeliveryStatus.Retrying || item.Status == QqBotDeliveryStatus.Leased),
                group.Count(item => item.Status == QqBotDeliveryStatus.Succeeded),
                group.Count(item => item.Status == QqBotDeliveryStatus.Failed)))
            .ToDictionaryAsync(item => item.AgentId, ct);

        return new QqBotAdminOverview(
            true,
            "agent-outbound-https-long-poll",
            ToView(settings),
            agents.Select(agent => ToView(agent, statistics.GetValueOrDefault(agent.Id))).ToArray(),
            groups.Select(ToView).ToArray());
    }

    public async Task<QqBotGlobalSettingsView> UpdateGlobalSettingsAsync(QqBotGlobalSettingsUpdate update, CancellationToken ct = default)
    {
        ValidateRange(update.LongPollSeconds, 1, 30, nameof(update.LongPollSeconds));
        ValidateRange(update.DeliveryLeaseSeconds, 15, 300, nameof(update.DeliveryLeaseSeconds));
        ValidateRange(update.MaxDeliveryAttempts, 1, 10, nameof(update.MaxDeliveryAttempts));
        ValidateRange(update.MaxMessageLength, 100, 4000, nameof(update.MaxMessageLength));
        ValidateRange(update.MaxPendingDeliveries, 100, 100000, nameof(update.MaxPendingDeliveries));
        ValidateRange(update.GroupCooldownMilliseconds, 0, 60000, nameof(update.GroupCooldownMilliseconds));
        ValidateRange(update.CompetitionCooldownMilliseconds, 0, 60000, nameof(update.CompetitionCooldownMilliseconds));
        ValidateRange(update.ManualNotificationCooldownSeconds, 1, 3600, nameof(update.ManualNotificationCooldownSeconds));

        var settings = await GetOrCreateGlobalSettingsAsync(ct);
        settings.Enabled = update.Enabled;
        settings.LongPollSeconds = update.LongPollSeconds;
        settings.DeliveryLeaseSeconds = update.DeliveryLeaseSeconds;
        settings.MaxDeliveryAttempts = update.MaxDeliveryAttempts;
        settings.MaxMessageLength = update.MaxMessageLength;
        settings.MaxPendingDeliveries = update.MaxPendingDeliveries;
        settings.GroupCooldownMilliseconds = update.GroupCooldownMilliseconds;
        settings.CompetitionCooldownMilliseconds = update.CompetitionCooldownMilliseconds;
        settings.ManualNotificationCooldownSeconds = update.ManualNotificationCooldownSeconds;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToView(settings);
    }

    public async Task<QqBotAgentView> UpsertAgentAsync(QqBotAgentUpdate update, CancellationToken ct = default)
    {
        var name = Normalize(update.Name, 80, "agent_name_invalid");
        var publicKey = NormalizePublicKey(update.PublicKeyPem);
        ValidateRange(update.PreviousKeyOverlapMinutes, 0, 1440, nameof(update.PreviousKeyOverlapMinutes));
        var now = DateTime.UtcNow;
        QqBotAgent agent;
        if (update.Id.HasValue)
        {
            agent = await db.QqBotAgents.FirstOrDefaultAsync(item => item.Id == update.Id.Value, ct)
                ?? throw new KeyNotFoundException("qqbot_agent_not_found");
            if (!string.Equals(agent.PublicKeyPem, publicKey, StringComparison.Ordinal))
            {
                agent.PreviousPublicKeyPem = agent.PublicKeyPem;
                agent.PreviousKeyValidUntil = update.PreviousKeyOverlapMinutes == 0
                    ? null
                    : now.AddMinutes(update.PreviousKeyOverlapMinutes);
            }
        }
        else
        {
            agent = new QqBotAgent { Id = Guid.NewGuid(), CreatedAt = now };
            db.QqBotAgents.Add(agent);
        }

        agent.Name = name;
        agent.Enabled = update.Enabled;
        agent.PublicKeyPem = publicKey;
        agent.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToView(agent, null);
    }

    public async Task<QqBotGroupView> SetGroupAuthorizationAsync(Guid groupId, bool isAuthorized, CancellationToken ct = default)
    {
        var group = await db.QqBotGroups.FirstOrDefaultAsync(item => item.Id == groupId, ct)
            ?? throw new KeyNotFoundException("qqbot_group_not_found");
        if (isAuthorized && !group.IsPresent)
            throw new InvalidOperationException("qqbot_group_not_present");
        group.IsAuthorized = isAuthorized;
        group.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToView(group);
    }

    public async Task<CompetitionQqBotConfigurationView> GetCompetitionConfigurationAsync(Guid competitionId, CancellationToken ct = default)
    {
        await EnsureCompetitionExistsAsync(competitionId, ct);
        return await BuildCompetitionConfigurationAsync(competitionId, ct);
    }

    public async Task<CompetitionQqBotConfigurationView> UpdateCompetitionConfigurationAsync(
        Guid competitionId,
        CompetitionQqBotConfigurationUpdate update,
        CancellationToken ct = default)
    {
        await EnsureCompetitionExistsAsync(competitionId, ct);
        if (update.EventRules.Select(item => item.EventType).Distinct().Count() != update.EventRules.Count)
            throw new ArgumentException("qqbot_duplicate_event_rule");
        if (update.GroupBindings.Select(item => item.GroupId).Distinct().Count() != update.GroupBindings.Count)
            throw new ArgumentException("qqbot_duplicate_group_binding");

        var templateIds = update.EventRules.Where(item => item.TemplateId.HasValue).Select(item => item.TemplateId!.Value).ToArray();
        if (templateIds.Length > 0)
        {
            var templates = await db.QqBotTemplates.AsNoTracking()
                .Where(item => templateIds.Contains(item.Id) && (item.CompetitionId == null || item.CompetitionId == competitionId))
                .ToDictionaryAsync(item => item.Id, ct);
            foreach (var rule in update.EventRules.Where(item => item.TemplateId.HasValue))
                if (!templates.TryGetValue(rule.TemplateId!.Value, out var template) || template.EventType != rule.EventType)
                    throw new ArgumentException("qqbot_template_scope_or_type_invalid");
        }

        var requestedGroupIds = update.GroupBindings.Select(item => item.GroupId).ToArray();
        var validGroups = await db.QqBotGroups.AsNoTracking()
            .Where(item => requestedGroupIds.Contains(item.Id) && item.IsPresent && item.IsAuthorized)
            .ToDictionaryAsync(item => item.Id, ct);
        foreach (var binding in update.GroupBindings)
        {
            if (!validGroups.TryGetValue(binding.GroupId, out var group) || group.AgentId != binding.AgentId || group.GroupId != binding.QqGroupId)
                throw new ArgumentException("qqbot_group_binding_invalid");
            if (binding.EventTypes.Distinct().Count() != binding.EventTypes.Count)
                throw new ArgumentException("qqbot_duplicate_binding_event_type");
        }

        var now = DateTime.UtcNow;
        var settings = await db.CompetitionQqBotSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.CompetitionId == competitionId, ct);
        if (settings is null)
        {
            settings = new CompetitionQqBotSettings { Id = Guid.NewGuid(), CompetitionId = competitionId, CreatedAt = now };
            db.CompetitionQqBotSettings.Add(settings);
        }
        settings.Enabled = update.Enabled;
        settings.AllowMessages = update.AllowMessages;
        settings.AllowManualNotifications = update.AllowManualNotifications;
        settings.StopNormalEventsAfterFinished = update.StopNormalEventsAfterFinished;
        settings.MentionAll = update.MentionAll;
        settings.ShowTeamName = update.ShowTeamName;
        settings.ShowUserName = update.ShowUserName;
        settings.ShowChallengeCategory = update.ShowChallengeCategory;
        settings.IncludeCompetitionLink = update.IncludeCompetitionLink;
        settings.IncludeChallengeLink = update.IncludeChallengeLink;
        settings.HidePenaltyDetails = update.HidePenaltyDetails;
        settings.UpdatedAt = now;

        var oldRules = await db.CompetitionQqBotEventRules.IgnoreQueryFilters()
            .Where(item => item.CompetitionId == competitionId).ToListAsync(ct);
        db.CompetitionQqBotEventRules.RemoveRange(oldRules);
        db.CompetitionQqBotEventRules.AddRange(update.EventRules.Select(rule => new CompetitionQqBotEventRule
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            EventType = rule.EventType,
            Enabled = rule.Enabled,
            TemplateId = rule.TemplateId,
            UpdatedAt = now
        }));

        var oldBindings = await db.CompetitionQqBotGroupBindings.IgnoreQueryFilters()
            .Where(item => item.CompetitionId == competitionId).ToListAsync(ct);
        db.CompetitionQqBotGroupBindings.RemoveRange(oldBindings);
        db.CompetitionQqBotGroupBindings.AddRange(update.GroupBindings.Select(binding => new CompetitionQqBotGroupBinding
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            AgentId = binding.AgentId,
            GroupId = binding.GroupId,
            IsDefault = binding.IsDefault,
            EventTypesJson = JsonSerializer.Serialize(binding.EventTypes.Distinct().Order().ToArray(), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        }));
        await db.SaveChangesAsync(ct);
        return await BuildCompetitionConfigurationAsync(competitionId, ct);
    }

    public async Task<IReadOnlyList<QqBotTemplateView>> GetTemplatesAsync(Guid? competitionId, CancellationToken ct = default)
    {
        var templates = await db.QqBotTemplates.AsNoTracking()
            .Where(item => item.CompetitionId == null || item.CompetitionId == competitionId)
            .OrderBy(item => item.EventType).ThenByDescending(item => item.CompetitionId.HasValue).ThenBy(item => item.Name)
            .ToListAsync(ct);
        var result = templates.Select(ToView).ToList();
        foreach (var eventType in EventTypes)
            result.Add(new QqBotTemplateView(null, null, eventType, "内置安全模板", QqBotDefaults.Template(eventType), false, true, QqBotDefaults.AllowedVariables(eventType), null));
        return result;
    }

    public async Task<QqBotTemplateView> UpsertTemplateAsync(QqBotTemplateUpdate update, Guid actorUserId, CancellationToken ct = default)
    {
        var name = Normalize(update.Name, 80, "template_name_invalid");
        var content = Normalize(update.Content, 4000, "template_content_invalid", preserveNewlines: true);
        _ = renderer.Render(update.EventType, content, SampleValues(update.EventType), false, 4000);
        if (update.CompetitionId.HasValue)
            await EnsureCompetitionExistsAsync(update.CompetitionId.Value, ct);
        var now = DateTime.UtcNow;
        QqBotTemplate template;
        if (update.Id.HasValue)
        {
            template = await db.QqBotTemplates.FirstOrDefaultAsync(item => item.Id == update.Id.Value, ct)
                ?? throw new KeyNotFoundException("qqbot_template_not_found");
            if (template.CompetitionId != update.CompetitionId)
                throw new InvalidOperationException("qqbot_template_scope_immutable");
        }
        else
        {
            template = new QqBotTemplate { Id = Guid.NewGuid(), CompetitionId = update.CompetitionId, CreatedAt = now };
            db.QqBotTemplates.Add(template);
        }
        if (update.IsDefault)
        {
            await db.QqBotTemplates.Where(item => item.Id != template.Id && item.CompetitionId == update.CompetitionId && item.EventType == update.EventType && item.IsDefault)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsDefault, false), ct);
        }
        template.EventType = update.EventType;
        template.Name = name;
        template.Content = content;
        template.IsDefault = update.IsDefault;
        template.UpdatedById = actorUserId;
        template.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToView(template);
    }

    public async Task<QqBotPreviewResult> PreviewAsync(QqBotPreviewRequest request, CancellationToken ct = default)
    {
        var global = await GetOrCreateGlobalSettingsAsync(ct);
        var template = request.TemplateId.HasValue
            ? await db.QqBotTemplates.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.TemplateId && item.EventType == request.EventType && (item.CompetitionId == null || item.CompetitionId == request.CompetitionId), ct)
            : null;
        var values = SampleValues(request.EventType);
        if (request.EventType == QqBotEventType.Announcement)
        {
            values["announcement_title"] = Normalize(request.AnnouncementTitle ?? "通知标题", 120, "announcement_title_invalid");
            values["announcement_content"] = Normalize(request.AnnouncementContent ?? "通知正文", 1800, "announcement_content_invalid", true);
        }
        var rendered = renderer.Render(request.EventType, template?.Content ?? QqBotDefaults.Template(request.EventType), values, false, global.MaxMessageLength);
        return new QqBotPreviewResult(rendered.Text, rendered.SegmentsJson, rendered.Text.Length, []);
    }

    public async Task<QqBotQueuedNotification> QueueManualNotificationAsync(QqBotManualNotificationRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var title = Normalize(request.Title, 120, "announcement_title_invalid");
        var content = Normalize(request.Content, 1800, "announcement_content_invalid", true);
        var targetIds = request.GroupIds.Distinct().ToArray();
        if (targetIds.Length == 0 || targetIds.Length > 20)
            throw new ArgumentException("announcement_group_count_invalid");
        var settings = await db.CompetitionQqBotSettings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.CompetitionId == request.CompetitionId, ct)
            ?? throw new InvalidOperationException("qqbot_competition_not_configured");
        if (!request.IsTest && !settings.AllowManualNotifications)
            throw new InvalidOperationException("qqbot_manual_notifications_disabled");
        var validCount = await db.CompetitionQqBotGroupBindings.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.CompetitionId == request.CompetitionId && targetIds.Contains(item.GroupId), ct);
        if (validCount != targetIds.Length)
            throw new ArgumentException("qqbot_target_group_not_bound");
        var global = await GetOrCreateGlobalSettingsAsync(ct);
        var cooldownCutoff = DateTime.UtcNow.AddSeconds(-global.ManualNotificationCooldownSeconds);
        if (await db.QqBotEvents.IgnoreQueryFilters().AnyAsync(item => item.CompetitionId == request.CompetitionId && item.ActorUserId == actorUserId && item.CreatedAt >= cooldownCutoff && (item.Source == QqBotDeliverySource.Manual || item.Source == QqBotDeliverySource.Test), ct))
            throw new InvalidOperationException("qqbot_manual_notification_rate_limited");

        var eventId = Guid.NewGuid();
        outbox.Add(new CompetitionNotification(
            request.CompetitionId, CompetitionNotificationTypes.Announcement, "announcement", eventId, actorUserId,
            $"manual:{actorUserId:N}:{eventId:N}",
            JsonSerializer.Serialize(new { announcement_title = title, announcement_content = content }, JsonOptions),
            request.IsTest ? "test" : "manual", targetIds, request.TemplateId, null, eventId));
        await db.SaveChangesAsync(ct);
        return new QqBotQueuedNotification(eventId, targetIds.Length, "queued");
    }

    public async Task<QqBotDeliveryLogPage> GetDeliveryLogsAsync(QqBotDeliveryLogFilter filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var query = from delivery in db.QqBotDeliveries.IgnoreQueryFilters().AsNoTracking()
                    join competition in db.Competitions.IgnoreQueryFilters().AsNoTracking() on delivery.CompetitionId equals competition.Id
                    join qgroup in db.QqBotGroups.AsNoTracking() on delivery.GroupId equals qgroup.Id
                    select new { delivery, CompetitionName = competition.Title, qgroup.GroupName };
        if (filter.CompetitionId.HasValue) query = query.Where(item => item.delivery.CompetitionId == filter.CompetitionId);
        if (filter.EventType.HasValue) query = query.Where(item => item.delivery.EventType == filter.EventType);
        if (filter.Status.HasValue) query = query.Where(item => item.delivery.Status == filter.Status);
        if (filter.From.HasValue) query = query.Where(item => item.delivery.CreatedAt >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(item => item.delivery.CreatedAt <= filter.To.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.delivery.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new QqBotDeliveryLogPage(items.Select(item => new QqBotDeliveryLogView(
            item.delivery.Id, item.delivery.CompetitionId, item.CompetitionName, item.delivery.EventId,
            item.delivery.EventType, item.delivery.Source, item.delivery.QqGroupId, item.GroupName,
            item.delivery.Status, item.delivery.AttemptCount, item.delivery.MaxAttempts,
            Summarize(item.delivery.RenderedText), item.delivery.MessageDigest, item.delivery.CreatedAt,
            item.delivery.UpdatedAt, item.delivery.SentAt, item.delivery.LastErrorCode,
            item.delivery.LastErrorSummary, item.delivery.RemoteMessageSequence,
            item.delivery.RemoteSentAt, item.delivery.RetriedByUserId)).ToArray(), page, pageSize, total);
    }

    public async Task<QqBotRetryResult> RetryDeliveryAsync(Guid deliveryId, Guid actorUserId, CancellationToken ct = default)
    {
        var delivery = await db.QqBotDeliveries.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == deliveryId, ct)
            ?? throw new KeyNotFoundException("qqbot_delivery_not_found");
        if (delivery.Status is not (QqBotDeliveryStatus.Failed or QqBotDeliveryStatus.Cancelled))
            throw new InvalidOperationException("qqbot_delivery_not_retryable");
        var originalEvent = await db.QqBotEvents.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(item => item.Id == delivery.EventId, ct);
        var eventId = Guid.NewGuid();
        outbox.Add(new CompetitionNotification(delivery.CompetitionId,
            EventName(delivery.EventType), originalEvent.SubjectType,
            originalEvent.SubjectId, actorUserId, $"manual-retry:{deliveryId:N}:{eventId:N}",
            originalEvent.PayloadJson, "manual-retry", new[] { delivery.GroupId },
            delivery.TemplateId, delivery.Id, eventId));
        await db.SaveChangesAsync(ct);
        return new QqBotRetryResult(eventId, deliveryId, "queued");
    }

    private async Task<QqBotGlobalSettings> GetOrCreateGlobalSettingsAsync(CancellationToken ct)
    {
        var settings = await db.QqBotGlobalSettings.FirstOrDefaultAsync(item => item.Id == QqBotDefaults.GlobalSettingsId, ct);
        if (settings is not null) return settings;
        settings = new QqBotGlobalSettings { Id = QqBotDefaults.GlobalSettingsId, UpdatedAt = DateTime.UtcNow };
        db.QqBotGlobalSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    private async Task EnsureCompetitionExistsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Competitions.IgnoreQueryFilters().AnyAsync(item => item.Id == id, ct))
            throw new KeyNotFoundException("competition_not_found");
    }

    private async Task<CompetitionQqBotConfigurationView> BuildCompetitionConfigurationAsync(Guid competitionId, CancellationToken ct)
    {
        var global = await GetOrCreateGlobalSettingsAsync(ct);
        var settings = await db.CompetitionQqBotSettings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.CompetitionId == competitionId, ct);
        var rules = await db.CompetitionQqBotEventRules.IgnoreQueryFilters().AsNoTracking().Where(item => item.CompetitionId == competitionId).ToListAsync(ct);
        var bindings = await (from binding in db.CompetitionQqBotGroupBindings.IgnoreQueryFilters().AsNoTracking()
                              join qgroup in db.QqBotGroups.AsNoTracking() on binding.GroupId equals qgroup.Id
                              where binding.CompetitionId == competitionId
                              select new { binding, qgroup.GroupId, qgroup.GroupName }).ToListAsync(ct);
        var onlineCutoff = DateTime.UtcNow.AddMinutes(-2);
        var online = await db.QqBotAgents.AsNoTracking().AnyAsync(item => item.Enabled && item.QqOnline && item.LastHeartbeatAt >= onlineCutoff, ct);
        var warnings = new List<string>();
        if (!global.Enabled) warnings.Add("global_plugin_disabled");
        if (!online) warnings.Add("no_online_agent");
        if (bindings.Count == 0) warnings.Add("no_bound_group");
        var eventRules = EventTypes.Select(type =>
        {
            var rule = rules.FirstOrDefault(item => item.EventType == type);
            return new QqBotEventRuleView(type, rule?.Enabled ?? false, rule?.TemplateId);
        }).ToArray();
        return new CompetitionQqBotConfigurationView(competitionId, global.Enabled, online,
            settings?.Enabled ?? false, settings?.AllowMessages ?? false,
            settings?.AllowManualNotifications ?? false, settings?.StopNormalEventsAfterFinished ?? true,
            settings?.MentionAll ?? false, settings?.ShowTeamName ?? true, settings?.ShowUserName ?? false,
            settings?.ShowChallengeCategory ?? true, settings?.IncludeCompetitionLink ?? true,
            settings?.IncludeChallengeLink ?? true, settings?.HidePenaltyDetails ?? true, eventRules,
            bindings.Select(item => new QqBotGroupBindingView(item.binding.Id, item.binding.AgentId,
                item.binding.GroupId, item.GroupId, item.GroupName, item.binding.IsDefault,
                DeserializeEventTypes(item.binding.EventTypesJson))).ToArray(), warnings, settings?.UpdatedAt);
    }

    private static IReadOnlyList<QqBotEventType> DeserializeEventTypes(string json)
    {
        try { return JsonSerializer.Deserialize<QqBotEventType[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Dictionary<string, string?> SampleValues(QqBotEventType type)
        => QqBotDefaults.AllowedVariables(type).ToDictionary(name => name, name => (string?)(name switch
        {
            "competition_name" => "示例比赛",
            "problem_title" => "示例题目",
            "problem_category" => "Web",
            "team_name" => "示例队伍",
            "user_name" => "示例选手",
            "blood_rank" => "1",
            "hint_title" => "新提示",
            "hint_content" => "这是一条公开提示。",
            "penalty_type" => "封禁",
            "penalty_reason" => "以主办方公告为准",
            "announcement_title" => "主办方通知",
            "announcement_content" => "这是一条测试通知。",
            "occurred_at" => "2026-01-01 00:00:00 UTC",
            "competition_url" => "https://example.invalid/competitions/demo",
            "problem_url" => "https://example.invalid/problems/demo",
            _ => string.Empty
        }), StringComparer.Ordinal);

    private static QqBotGlobalSettingsView ToView(QqBotGlobalSettings item) => new(item.Enabled, item.LongPollSeconds,
        item.DeliveryLeaseSeconds, item.MaxDeliveryAttempts, item.MaxMessageLength, item.MaxPendingDeliveries,
        item.GroupCooldownMilliseconds, item.CompetitionCooldownMilliseconds, item.ManualNotificationCooldownSeconds, item.UpdatedAt);
    private static QqBotAgentView ToView(QqBotAgent item, AgentStatistics? stats) => new(item.Id, item.Name, item.Enabled,
        !string.IsNullOrWhiteSpace(item.PublicKeyPem), !string.IsNullOrWhiteSpace(item.PreviousPublicKeyPem),
        item.PreviousKeyValidUntil, item.LastHeartbeatAt, item.LastHeartbeatAt >= DateTime.UtcNow.AddMinutes(-2), item.QqOnline,
        item.BotUin, item.BotNickname, item.ImplementationName, item.ImplementationVersion, item.MilkyVersion,
        item.LastErrorCode, item.LastErrorSummary, stats?.Pending ?? 0, stats?.Succeeded ?? 0, stats?.Failed ?? 0,
        item.CreatedAt, item.UpdatedAt);
    private static QqBotGroupView ToView(QqBotGroup item) => new(item.Id, item.AgentId, item.GroupId, item.GroupName,
        item.IsPresent, item.IsAuthorized, item.LastSeenAt);
    private static QqBotTemplateView ToView(QqBotTemplate item) => new(item.Id, item.CompetitionId, item.EventType,
        item.Name, item.Content, item.IsDefault, false, QqBotDefaults.AllowedVariables(item.EventType), item.UpdatedAt);

    private static string NormalizePublicKey(string pem)
    {
        var normalized = Normalize(pem, 2000, "agent_public_key_invalid", true);
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(normalized);
            if (key.KeySize != 256) throw new CryptographicException();
            return key.ExportSubjectPublicKeyInfoPem();
        }
        catch (CryptographicException) { throw new ArgumentException("agent_public_key_invalid"); }
    }

    private static string Normalize(string value, int max, string error, bool preserveNewlines = false)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(error);
        var normalized = value.Normalize(NormalizationForm.FormC).Trim();
        if (normalized.Length > max || normalized.Any(ch => char.IsControl(ch) && (!preserveNewlines || ch is not ('\r' or '\n' or '\t'))))
            throw new ArgumentException(error);
        return normalized;
    }
    private static void ValidateRange(int value, int min, int max, string name)
    { if (value < min || value > max) throw new ArgumentOutOfRangeException(name); }
    private static string Summarize(string value) => value.Length <= 160 ? value : value[..160] + "…";
    private static string EventName(QqBotEventType type) => type switch
    {
        QqBotEventType.CompetitionStarted => CompetitionNotificationTypes.CompetitionStarted,
        QqBotEventType.ChallengePublished => CompetitionNotificationTypes.ChallengePublished,
        QqBotEventType.HintPublished => CompetitionNotificationTypes.HintPublished,
        QqBotEventType.FirstBlood => CompetitionNotificationTypes.FirstBlood,
        QqBotEventType.SecondBlood => CompetitionNotificationTypes.SecondBlood,
        QqBotEventType.ThirdBlood => CompetitionNotificationTypes.ThirdBlood,
        QqBotEventType.TeamPenalized => CompetitionNotificationTypes.TeamPenalized,
        QqBotEventType.Announcement => CompetitionNotificationTypes.Announcement,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private sealed record AgentStatistics(Guid AgentId, int Pending, int Succeeded, int Failed);
}
