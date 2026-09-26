using System.Globalization;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Logs;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Hosting.Observability;

/// <summary>Clears untrusted structured state before OTLP export and mirrors redacted live events.</summary>
public sealed class RedactedPlatformLogProcessor(
    PlatformLogBroadcastQueue broadcasts,
    PlatformLogService defaultService,
    PlatformLogUserIdProtector userIds) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        var category = record.CategoryName ?? string.Empty;
        if (category.StartsWith(typeof(PlatformLogBroadcastAgent).FullName!,
                StringComparison.Ordinal))
        {
            record.Body = string.Empty;
            record.FormattedMessage = string.Empty;
            record.Attributes = [];
            record.Exception = null;
            return;
        }

        var properties = record.Attributes?.Select(item =>
            new KeyValuePair<string, object?>(item.Key, item.Value)).ToList() ?? [];
        record.ForEachScope(static (scope, target) =>
        {
            if (scope.Scope is IEnumerable<KeyValuePair<string, object?>> values)
                target.AddRange(values);
        }, properties);
        var timestamp = record.Timestamp == default
            ? DateTimeOffset.UtcNow : record.Timestamp;
        var level = record.LogLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => PlatformLogLevel.Trace,
            Microsoft.Extensions.Logging.LogLevel.Debug => PlatformLogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => PlatformLogLevel.Information,
            Microsoft.Extensions.Logging.LogLevel.Warning => PlatformLogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => PlatformLogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => PlatformLogLevel.Critical,
            _ => PlatformLogLevel.Information
        };
        var message = Limit(PlatformLogRedactor.Redact(
            record.FormattedMessage ?? record.Body, properties), 16_384);
        // Exception.ToString() can embed EF command text, parameters, or provider
        // details. Preserve the type for diagnostics without exporting that payload.
        string? exceptionMessage = null;
        var service = category.StartsWith("NoCTF.API", StringComparison.Ordinal)
            ? PlatformLogService.Api
            : category.StartsWith("NoCTF.Worker", StringComparison.Ordinal)
                ? PlatformLogService.Worker
                : category.StartsWith("NoCTF.Runner", StringComparison.Ordinal)
                    ? PlatformLogService.Runner : defaultService;
        var view = new PlatformLogView(
            timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" +
            Guid.NewGuid().ToString("N"),
            timestamp, service, level, Limit(category, 512),
            record.EventId.Id, record.EventId.Name, message,
            record.Exception?.GetType().FullName, exceptionMessage,
            ReadGuid(properties, "CompetitionId"),
            ReadGuid(properties, "RuntimeInstanceId"),
            ReadGuid(properties, "TeamId"),
            ReadGuid(properties, "UserId", "ActorUserId", "RelatedUserId"),
            ReadGuid(properties, "CompetitionChallengeId"),
            ReadGuid(properties, "GameplayFactId"));
        var stored = new StoredPlatformLog(
            view with { UserId = null },
            view.UserId is Guid userId ? userIds.Protect(userId) : null);
        var safe = JsonSerializer.Serialize(stored,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        record.Attributes = [];
        record.Exception = null;
        record.Body = safe;
        record.FormattedMessage = safe;
        broadcasts.TryWrite(view);
    }

    private static Guid? ReadGuid(
        IEnumerable<KeyValuePair<string, object?>> properties, params string[] names)
    {
        foreach (var property in properties)
        {
            if (!names.Contains(property.Key, StringComparer.OrdinalIgnoreCase))
                continue;
            if (property.Value is Guid guid)
                return guid;
            if (Guid.TryParse(Convert.ToString(property.Value,
                    CultureInfo.InvariantCulture), out guid))
                return guid;
        }
        return null;
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
