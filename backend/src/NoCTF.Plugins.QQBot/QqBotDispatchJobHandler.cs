using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.QQBot;

internal sealed class QqBotDispatchJobHandler(
    ApplicationDbContext db,
    QqBotTemplateRenderer renderer,
    IConfiguration configuration,
    ILogger<QqBotDispatchJobHandler> logger) : ICompetitionJobHandler
{
    public const string JobKeyValue = "qqbot.dispatch-event";
    public string JobKey => JobKeyValue;

    public async Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<QqBotDispatchPayload>(task.PayloadJson, JsonOptions)
                      ?? throw new InvalidOperationException("QQBot dispatch payload is invalid.");
        var botEvent = await db.QqBotEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == payload.EventId, ct);
        if (botEvent is null || botEvent.Status != QqBotEventStatus.Pending)
            return;

        var global = await db.QqBotGlobalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == QqBotDefaults.GlobalSettingsId, ct);
        if (global is null || !global.Enabled)
        {
            await SuppressAsync(botEvent, "global_disabled", "QQBot integration is disabled.", ct);
            return;
        }

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == botEvent.CompetitionId, ct);
        if (competition is null)
        {
            await FailAsync(botEvent, "competition_missing", "Competition no longer exists.", ct);
            return;
        }

        var competitionSettings = await db.CompetitionQqBotSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CompetitionId == botEvent.CompetitionId, ct);
        if (!IsEventAllowed(botEvent, competition, competitionSettings, out var suppressionCode))
        {
            await SuppressAsync(botEvent, suppressionCode!, "Competition QQBot settings suppressed this event.", ct);
            return;
        }

        CompetitionQqBotEventRule? rule = null;
        if (botEvent.Source == QqBotDeliverySource.Automatic)
        {
            rule = await db.CompetitionQqBotEventRules
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.CompetitionId == botEvent.CompetitionId &&
                    item.EventType == botEvent.EventType, ct);
            if (rule is null || !rule.Enabled)
            {
                await SuppressAsync(botEvent, "event_disabled", "The event type is disabled for this competition.", ct);
                return;
            }
        }

        var bindings = await ResolveBindingsAsync(botEvent, ct);
        if (bindings.Count == 0)
        {
            await FailAsync(botEvent, "no_authorized_group", "No authorized and present group is bound to this event.", ct);
            return;
        }

        var pendingCount = await db.QqBotDeliveries
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.Status == QqBotDeliveryStatus.Pending ||
                item.Status == QqBotDeliveryStatus.Retrying ||
                item.Status == QqBotDeliveryStatus.Leased, ct);
        if (pendingCount + bindings.Count > global.MaxPendingDeliveries)
        {
            await FailAsync(botEvent, "queue_capacity", "QQBot delivery queue capacity was reached.", ct);
            return;
        }

        var template = await ResolveTemplateAsync(botEvent, rule?.TemplateId, ct);
        var values = BuildValues(botEvent, competition, competitionSettings);
        QqBotRenderedMessage rendered;
        try
        {
            rendered = renderer.Render(
                botEvent.EventType,
                template.Content,
                values,
                competitionSettings?.MentionAll == true,
                global.MaxMessageLength);
        }
        catch (QqBotTemplateRenderException exception)
        {
            logger.LogWarning(
                "QQBot template {TemplateId} failed for event {EventId}: {ErrorCode}; using the built-in template.",
                template.Id,
                botEvent.Id,
                exception.Code);
            try
            {
                rendered = renderer.Render(
                    botEvent.EventType,
                    QqBotDefaults.Template(botEvent.EventType),
                    values,
                    competitionSettings?.MentionAll == true,
                    global.MaxMessageLength);
                template = new ResolvedTemplate(null, QqBotDefaults.Template(botEvent.EventType));
            }
            catch (QqBotTemplateRenderException fallbackException)
            {
                await FailAsync(botEvent, fallbackException.Code, "The configured and built-in templates could not be rendered.", ct);
                return;
            }
        }

        var now = DateTime.UtcNow;
        var deliveryKeys = bindings
            .Select(binding => DeliveryIdempotencyKey(botEvent, binding))
            .ToArray();
        var existingKeyList = await db.QqBotDeliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => deliveryKeys.Contains(item.IdempotencyKey))
            .Select(item => item.IdempotencyKey)
            .ToListAsync(ct);
        var existingKeys = existingKeyList.ToHashSet(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            var idempotencyKey = DeliveryIdempotencyKey(botEvent, binding);
            if (existingKeys.Contains(idempotencyKey))
                continue;
            db.QqBotDeliveries.Add(new QqBotDelivery
            {
                Id = Guid.NewGuid(),
                CompetitionId = botEvent.CompetitionId,
                EventId = botEvent.Id,
                AgentId = binding.AgentId,
                GroupId = binding.GroupId,
                QqGroupId = binding.QqGroupId,
                EventType = botEvent.EventType,
                Source = botEvent.Source,
                TemplateId = template.Id,
                RenderedText = rendered.Text,
                RenderedSegmentsJson = rendered.SegmentsJson,
                MessageDigest = rendered.Digest,
                IdempotencyKey = idempotencyKey,
                Status = QqBotDeliveryStatus.Pending,
                MaxAttempts = global.MaxDeliveryAttempts,
                AvailableAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        botEvent.Status = QqBotEventStatus.Expanded;
        botEvent.ExpandedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
        {
            logger.LogInformation(exception, "QQBot delivery idempotency conflict for event {EventId}.", botEvent.Id);
            db.ChangeTracker.Clear();
            await db.QqBotEvents
                .IgnoreQueryFilters()
                .Where(item => item.Id == botEvent.Id && item.Status == QqBotEventStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, QqBotEventStatus.Expanded)
                    .SetProperty(item => item.ExpandedAt, now), CancellationToken.None);
        }
    }

    private static bool IsEventAllowed(
        QqBotEvent botEvent,
        Competition competition,
        CompetitionQqBotSettings? settings,
        out string? suppressionCode)
    {
        suppressionCode = null;
        if (botEvent.Source == QqBotDeliverySource.Test)
            return true;
        if (settings is null || !settings.Enabled || !settings.AllowMessages)
        {
            suppressionCode = "competition_disabled";
            return false;
        }
        if (botEvent.Source is QqBotDeliverySource.Manual or QqBotDeliverySource.ManualRetry)
        {
            if (!settings.AllowManualNotifications)
            {
                suppressionCode = "manual_notifications_disabled";
                return false;
            }
            return true;
        }
        if (competition.Status == CompetitionStatus.Finished && settings.StopNormalEventsAfterFinished)
        {
            suppressionCode = "competition_finished";
            return false;
        }
        return true;
    }

    private async Task<List<ResolvedBinding>> ResolveBindingsAsync(QqBotEvent botEvent, CancellationToken ct)
    {
        HashSet<Guid>? requestedGroupIds = null;
        if (!string.IsNullOrWhiteSpace(botEvent.TargetGroupIdsJson))
        {
            requestedGroupIds = (JsonSerializer.Deserialize<Guid[]>(botEvent.TargetGroupIdsJson, JsonOptions) ?? [])
                .ToHashSet();
        }

        var rows = await (
            from binding in db.CompetitionQqBotGroupBindings.IgnoreQueryFilters().AsNoTracking()
            join qgroup in db.QqBotGroups.AsNoTracking() on binding.GroupId equals qgroup.Id
            join agent in db.QqBotAgents.AsNoTracking() on binding.AgentId equals agent.Id
            where binding.CompetitionId == botEvent.CompetitionId &&
                  qgroup.IsAuthorized && qgroup.IsPresent && agent.Enabled
            select new { binding, qgroup.GroupName, QqGroupId = qgroup.GroupId })
            .ToListAsync(ct);

        if (requestedGroupIds is not null)
            rows = rows.Where(row => requestedGroupIds.Contains(row.binding.GroupId)).ToList();
        else
        {
            var eventSpecific = rows
                .Where(row => ParseEventTypes(row.binding.EventTypesJson).Contains(botEvent.EventType))
                .ToList();
            rows = eventSpecific.Count > 0
                ? eventSpecific
                : rows.Where(row => row.binding.IsDefault).ToList();
        }

        return rows.Select(row => new ResolvedBinding(
                row.binding.AgentId,
                row.binding.GroupId,
                row.QqGroupId,
                row.GroupName))
            .ToList();
    }

    private async Task<ResolvedTemplate> ResolveTemplateAsync(
        QqBotEvent botEvent,
        Guid? ruleTemplateId,
        CancellationToken ct)
    {
        var requestedId = botEvent.RequestedTemplateId ?? ruleTemplateId;
        if (requestedId.HasValue)
        {
            var requested = await db.QqBotTemplates.AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.Id == requestedId.Value &&
                    item.EventType == botEvent.EventType &&
                    (item.CompetitionId == null || item.CompetitionId == botEvent.CompetitionId), ct);
            if (requested is not null)
                return new ResolvedTemplate(requested.Id, requested.Content);
        }

        var scopedDefault = await db.QqBotTemplates.AsNoTracking()
            .Where(item =>
                item.EventType == botEvent.EventType &&
                item.IsDefault &&
                (item.CompetitionId == botEvent.CompetitionId || item.CompetitionId == null))
            .OrderByDescending(item => item.CompetitionId.HasValue)
            .ThenByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        return scopedDefault is null
            ? new ResolvedTemplate(null, QqBotDefaults.Template(botEvent.EventType))
            : new ResolvedTemplate(scopedDefault.Id, scopedDefault.Content);
    }

    private Dictionary<string, string?> BuildValues(
        QqBotEvent botEvent,
        Competition competition,
        CompetitionQqBotSettings? settings)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(botEvent.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!QqBotDefaults.AllowedVariables(botEvent.EventType).Contains(property.Name, StringComparer.Ordinal))
                        continue;
                    values[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => string.Empty
                    };
                }
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"QQBot event {botEvent.Id} contains invalid payload JSON.");
        }

        values["competition_name"] = competition.Title;
        values["occurred_at"] = botEvent.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        values["competition_url"] = BuildPublicUrl($"/competitions/{competition.Id:D}");
        if (botEvent.SubjectType == "challenge" && botEvent.SubjectId.HasValue)
            values["problem_url"] = BuildPublicUrl($"/competitions/{competition.Id:D}?challenge={botEvent.SubjectId.Value:D}");

        if (settings is not null)
        {
            if (!settings.ShowTeamName)
                values["team_name"] = "参赛队伍";
            if (!settings.ShowUserName)
                values["user_name"] = string.Empty;
            if (!settings.ShowChallengeCategory)
                values["problem_category"] = string.Empty;
            if (!settings.IncludeCompetitionLink)
                values["competition_url"] = string.Empty;
            if (!settings.IncludeChallengeLink)
                values["problem_url"] = string.Empty;
            if (settings.HidePenaltyDetails && botEvent.EventType == QqBotEventType.TeamPenalized)
                values["penalty_reason"] = "详情以主办方公告为准";
        }

        return values;
    }

    private string BuildPublicUrl(string relativePath)
    {
        var configured = configuration["QqBot:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(configured))
            return relativePath;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("QqBot:PublicBaseUrl must be an absolute HTTP(S) URL without credentials.");
        }
        return new Uri(baseUri, relativePath).ToString();
    }

    private static HashSet<QqBotEventType> ParseEventTypes(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<QqBotEventType[]>(json, JsonOptions) ?? []).ToHashSet();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string DeliveryIdempotencyKey(QqBotEvent botEvent, ResolvedBinding binding)
        => $"qqbot:{botEvent.IdempotencyKey}:{binding.AgentId:N}:{binding.GroupId:N}";

    private async Task SuppressAsync(QqBotEvent botEvent, string code, string summary, CancellationToken ct)
    {
        botEvent.Status = QqBotEventStatus.Suppressed;
        botEvent.LastErrorCode = code;
        botEvent.LastErrorSummary = summary;
        botEvent.ExpandedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(QqBotEvent botEvent, string code, string summary, CancellationToken ct)
    {
        botEvent.Status = QqBotEventStatus.Failed;
        botEvent.LastErrorCode = code;
        botEvent.LastErrorSummary = summary;
        botEvent.ExpandedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private sealed record ResolvedBinding(Guid AgentId, Guid GroupId, long QqGroupId, string GroupName);
    private sealed record ResolvedTemplate(Guid? Id, string Content);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
