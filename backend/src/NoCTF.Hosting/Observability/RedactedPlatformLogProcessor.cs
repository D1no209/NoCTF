using System.Globalization;
using System.Diagnostics;
using System.Data.Common;
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
        var message = (category, record.EventId.Id) switch
        {
            ("Microsoft.EntityFrameworkCore.Update", 10000) =>
                "Database save failure details were suppressed before log export.",
            ("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", 1) =>
                "Unhandled request failure details were suppressed before log export.",
            _ => Limit(SanitizeMessage(category,
                record.FormattedMessage ?? record.Body, properties), 16_384)
        };
        var hasException = record.Exception is not null;
        if ((hasException || ShouldIncludeDatabaseState(category, record.EventId.Id))
            && ReadSqlState(record.Exception) is { } sqlState)
            message = $"{message} SQLSTATE: {sqlState}.";
        if (hasException && ReadDatabaseObjects(record.Exception!) is { } databaseObjects)
            message = $"{message} Database objects: {databaseObjects}.";
        if ((hasException || ShouldIncludeDiagnosticSource(category, record.EventId.Id))
            && ReadDiagnosticSource() is { } source)
            message = $"{message} Operation source: {source}.";
        if (hasException)
        {
            message = $"{message} Exception chain: {ReadExceptionChain(record.Exception!)}.";
            if (ReadApplicationFrames(record.Exception!) is { } frames)
                message = $"{message} Application frames: {frames}.";
        }
        if (record.LogLevel >= Microsoft.Extensions.Logging.LogLevel.Warning
            && ReadTraceId(record) is { } traceId)
            message = $"{message} Trace: {traceId}.";
        message = Limit(message, 16_384);
        // Exception.ToString() can embed EF command text, parameters, or provider
        // details. Preserve the type for diagnostics without exporting that payload.
        string? exceptionMessage = null;
        var service = category.StartsWith("NoCTF.API", StringComparison.Ordinal)
            || category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"
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

    private static string? ReadDiagnosticSource()
    {
        var activity = Activity.Current;
        if (activity is null)
            return null;
        var source = activity.GetTagItem("noctf.endpoint")?.ToString()
            ?? activity.GetTagItem("messaging.message.type")?.ToString();
        if (string.IsNullOrWhiteSpace(source)
            && activity.DisplayName.StartsWith("NoCTF.", StringComparison.Ordinal))
            source = activity.DisplayName;
        if (string.IsNullOrWhiteSpace(source))
            return null;
        return Limit(source, 512);
    }

    private static bool ShouldIncludeDiagnosticSource(string category, int eventId) =>
        category switch
        {
            "Microsoft.EntityFrameworkCore.Query" => eventId == 20504,
            "Microsoft.EntityFrameworkCore.Database.Command" => eventId == 20102,
            "Microsoft.EntityFrameworkCore.Database.Transaction" => eventId == 20205,
            "Microsoft.EntityFrameworkCore.Update" => eventId == 10000,
            "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware" => eventId == 1,
            _ => false
        };

    private static bool ShouldIncludeDatabaseState(string category, int eventId) =>
        category == "Microsoft.EntityFrameworkCore.Database.Command" && eventId == 20102
        || category == "Microsoft.EntityFrameworkCore.Update" && eventId == 10000;

    private static string? ReadSqlState(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not DbException { SqlState: { Length: 5 } state }
                || !state.All(char.IsAsciiLetterOrDigit))
                continue;
            return state;
        }
        return null;
    }

    /// <summary>Exception type names from outermost to innermost; never the exception text.</summary>
    private static string ReadExceptionChain(Exception exception)
    {
        var types = new List<string>();
        for (var current = exception; current is not null && types.Count < 8;
             current = current.InnerException)
            types.Add(current.GetType().FullName ?? current.GetType().Name);
        return Limit(string.Join(" -> ", types), 2_048);
    }

    // Npgsql publishes PostgreSQL error fields through Exception.Data. Only schema identifiers are
    // exported; MessageText, Detail, Where and Hint can carry row values and stay suppressed.
    private static readonly string[] DatabaseObjectKeys =
        ["SchemaName", "TableName", "ColumnName", "ConstraintName", "DataTypeName"];

    private static string? ReadDatabaseObjects(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not DbException)
                continue;
            var values = new List<string>();
            foreach (var key in DatabaseObjectKeys)
            {
                if (current.Data.Contains(key)
                    && current.Data[key] is string value
                    && IsIdentifier(value))
                    values.Add($"{key}={value}");
            }
            return values.Count == 0 ? null : string.Join(", ", values);
        }
        return null;
    }

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '$');

    /// <summary>NoCTF method names (and line numbers when symbols exist) from the exception chain.</summary>
    private static string? ReadApplicationFrames(Exception exception)
    {
        var frames = new List<string>();
        for (var current = exception; current is not null && frames.Count < 8;
             current = current.InnerException)
        {
            foreach (var frame in new StackTrace(current, fNeedFileInfo: true).GetFrames())
            {
                if (frames.Count >= 8)
                    break;
                if (FormatApplicationFrame(frame) is { } text && !frames.Contains(text))
                    frames.Add(text);
            }
        }
        return frames.Count == 0 ? null : Limit(string.Join(" <- ", frames), 4_096);
    }

    private static string? FormatApplicationFrame(StackFrame frame)
    {
        var method = frame.GetMethod();
        var type = method?.DeclaringType;
        if (method is null || type is null)
            return null;
        var name = method.Name;
        // Async/iterator state machines: <ExecuteAsync>d__4.MoveNext -> Owner.ExecuteAsync
        if (type.Name.StartsWith('<') && type.DeclaringType is { } owner)
        {
            var end = type.Name.IndexOf('>');
            if (end > 1)
                name = type.Name[1..end];
            type = owner;
        }
        var typeName = type.FullName ?? type.Name;
        if (!typeName.StartsWith("NoCTF.", StringComparison.Ordinal))
            return null;
        var line = frame.GetFileLineNumber();
        return line > 0 ? $"{typeName}.{name}:{line}" : $"{typeName}.{name}";
    }

    private static string? ReadTraceId(LogRecord record)
    {
        var traceId = record.TraceId != default ? record.TraceId : Activity.Current?.TraceId ?? default;
        return traceId == default ? null : traceId.ToHexString();
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string SanitizeMessage(
        string category,
        string? message,
        IEnumerable<KeyValuePair<string, object?>> properties)
    {
        if (category.Equals("Microsoft.EntityFrameworkCore.Database.Command",
                StringComparison.Ordinal)
            || category.StartsWith("Npgsql.Command", StringComparison.Ordinal))
            return "Database command details were suppressed before log export.";

        return PlatformLogRedactor.Redact(message, properties);
    }
}
