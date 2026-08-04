using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Administration.PlatformLogs;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Observability;

public sealed record PlatformLogWriterOptions(
    PlatformLogService Service,
    int MaximumEntries);

public static class PlatformLoggingRegistration
{
    public static IServiceCollection AddNoCtfPlatformLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        PlatformLogService service)
    {
        var maximumEntries = configuration.GetValue("PlatformLogs:MaximumEntries", 100_000);
        if (maximumEntries is < 1_000 or > 1_000_000)
            throw new InvalidOperationException(
                "PlatformLogs:MaximumEntries must be between 1000 and 1000000.");
        services.AddSingleton(new PlatformLogWriterOptions(service, maximumEntries));
        services.AddSingleton<ILoggerProvider, RedisPlatformLoggerProvider>();
        return services;
    }
}

public sealed class RedisPlatformLoggerProvider(
    IConnectionMultiplexer redis,
    PlatformLogWriterOptions options)
    : ILoggerProvider, ISupportExternalScope
{
    public const string StreamKey = "platform-logs:v1";
    public const string Channel = "platform-logs:v1:live";
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) =>
        new RedisPlatformLogger(categoryName, redis, options, () => scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider provider) =>
        scopeProvider = provider;

    public void Dispose()
    {
    }

    private sealed class RedisPlatformLogger(
        string category,
        IConnectionMultiplexer redis,
        PlatformLogWriterOptions options,
        Func<IExternalScopeProvider> scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            scopes().Push(state);

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None
            && !category.StartsWith(
                "NoCTF.Infrastructure.Observability.RedisPlatform",
                StringComparison.Ordinal)
            && (logLevel >= LogLevel.Warning
                || category.StartsWith("NoCTF.", StringComparison.Ordinal)
                    && logLevel >= LogLevel.Information);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var properties = new List<KeyValuePair<string, object?>>();
            AddProperties(state, properties);
            scopes().ForEachScope(
                static (scope, target) => AddProperties(scope, target),
                properties);
            var timestamp = DateTimeOffset.UtcNow;
            var message = Limit(
                PlatformLogRedactor.Redact(formatter(state, exception), properties),
                16_384);
            var exceptionMessage = exception is null
                ? null
                : Limit(PlatformLogRedactor.Redact(exception.ToString(), properties), 32_768);
            var competitionId = ReadGuid(properties, "CompetitionId");
            var runtimeInstanceId = ReadGuid(properties, "RuntimeInstanceId");
            var level = ToPlatformLevel(logLevel);
            if (!redis.IsConnected)
                return;
            var database = redis.GetDatabase();
            try
            {
                var cursor = database.StreamAdd(
                    StreamKey,
                    [
                        new("timestamp", timestamp.ToString("O", CultureInfo.InvariantCulture)),
                        new("service", ((short)options.Service).ToString(CultureInfo.InvariantCulture)),
                        new("level", ((short)level).ToString(CultureInfo.InvariantCulture)),
                        new("category", Limit(category, 512)),
                        new("eventId", eventId.Id.ToString(CultureInfo.InvariantCulture)),
                        new("eventName", eventId.Name ?? string.Empty),
                        new("message", message),
                        new("exceptionType", exception?.GetType().FullName ?? string.Empty),
                        new("exceptionMessage", exceptionMessage ?? string.Empty),
                        new("competitionId", competitionId?.ToString("D") ?? string.Empty),
                        new("runtimeInstanceId", runtimeInstanceId?.ToString("D") ?? string.Empty)
                    ],
                    maxLength: options.MaximumEntries,
                    useApproximateMaxLength: true);
                var notification = new PlatformLogView(
                    cursor.ToString(),
                    timestamp,
                    options.Service,
                    level,
                    Limit(category, 512),
                    eventId.Id,
                    eventId.Name,
                    message,
                    exception?.GetType().FullName,
                    exceptionMessage,
                    competitionId,
                    runtimeInstanceId);
                redis.GetSubscriber().Publish(
                    RedisChannel.Literal(Channel),
                    System.Text.Json.JsonSerializer.Serialize(notification, JsonOptions));
            }
            catch (RedisException)
            {
                // Logging must never change the outcome of the operation being logged.
            }
        }

        private static void AddProperties<TState>(
            TState state,
            List<KeyValuePair<string, object?>> properties)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                properties.AddRange(values);
        }

        private static Guid? ReadGuid(
            IEnumerable<KeyValuePair<string, object?>> properties,
            string key)
        {
            foreach (var property in properties)
            {
                if (!string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (property.Value is Guid guid)
                    return guid;
                if (Guid.TryParse(Convert.ToString(
                        property.Value,
                        CultureInfo.InvariantCulture), out guid))
                    return guid;
            }
            return null;
        }

        private static PlatformLogLevel ToPlatformLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => PlatformLogLevel.Trace,
            LogLevel.Debug => PlatformLogLevel.Debug,
            LogLevel.Information => PlatformLogLevel.Information,
            LogLevel.Warning => PlatformLogLevel.Warning,
            LogLevel.Error => PlatformLogLevel.Error,
            LogLevel.Critical => PlatformLogLevel.Critical,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };

        private static string Limit(string value, int maximumLength) =>
            value.Length <= maximumLength ? value : value[..maximumLength];
    }
}

public sealed class RedisPlatformLogStore(IConnectionMultiplexer redis) : IPlatformLogReader
{
    private const int ScanBatchSize = 500;
    private const int MaximumScannedEntries = 20_000;

    public async Task<PlatformLogQueryResult> QueryAsync(
        PlatformLogQuery query,
        CancellationToken ct)
    {
        try
        {
            var database = redis.GetDatabase();
            RedisValue minimumId = query.From is null
                ? "-"
                : $"{query.From.Value.ToUnixTimeMilliseconds()}-0";
            RedisValue maximumId = query.Cursor is not null
                ? $"({query.Cursor}"
                : query.To is null
                    ? "+"
                    : $"{query.To.Value.ToUnixTimeMilliseconds()}-999999999";
            var items = new List<PlatformLogView>(query.Limit);
            var scanned = 0;
            string? lastCursor = null;
            while (items.Count < query.Limit && scanned < MaximumScannedEntries)
            {
                ct.ThrowIfCancellationRequested();
                var entries = await database.StreamRangeAsync(
                    RedisPlatformLoggerProvider.StreamKey,
                    minimumId,
                    maximumId,
                    ScanBatchSize,
                    Order.Descending);
                if (entries.Length == 0)
                    break;
                scanned += entries.Length;
                foreach (var entry in entries)
                {
                    lastCursor = entry.Id.ToString();
                    if (TryMap(entry, out var item) && Matches(item, query))
                    {
                        items.Add(item);
                        if (items.Count == query.Limit)
                            break;
                    }
                }
                maximumId = $"({entries[^1].Id}";
                if (entries.Length < ScanBatchSize)
                    break;
            }
            return new(
                PlatformLogReadState.Available,
                items,
                items.Count == query.Limit || scanned >= MaximumScannedEntries
                    ? lastCursor
                    : null);
        }
        catch (RedisException)
        {
            return new(PlatformLogReadState.Unavailable, []);
        }
    }

    private static bool Matches(PlatformLogView item, PlatformLogQuery query) =>
        item.Level >= query.MinimumLevel
        && (query.Service is null || item.Service == query.Service)
        && (query.From is null || item.Timestamp >= query.From)
        && (query.To is null || item.Timestamp <= query.To)
        && (query.CompetitionId is null || item.CompetitionId == query.CompetitionId)
        && (query.RuntimeInstanceId is null
            || item.RuntimeInstanceId == query.RuntimeInstanceId);

    private static bool TryMap(StreamEntry entry, out PlatformLogView item)
    {
        var values = entry.Values.ToDictionary(
            value => value.Name.ToString(),
            value => value.Value.ToString(),
            StringComparer.Ordinal);
        if (!values.TryGetValue("timestamp", out var timestampText)
            || !DateTimeOffset.TryParse(
                timestampText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp)
            || !TryReadEnum(values, "service", out PlatformLogService service)
            || !TryReadEnum(values, "level", out PlatformLogLevel level)
            || !values.TryGetValue("category", out var category)
            || !values.TryGetValue("message", out var message))
        {
            item = null!;
            return false;
        }

        _ = values.TryGetValue("eventId", out var eventIdText);
        _ = int.TryParse(eventIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventId);
        values.TryGetValue("eventName", out var eventName);
        values.TryGetValue("exceptionType", out var exceptionType);
        values.TryGetValue("exceptionMessage", out var exceptionMessage);
        values.TryGetValue("competitionId", out var competitionIdText);
        values.TryGetValue("runtimeInstanceId", out var runtimeInstanceIdText);
        item = new(
            entry.Id.ToString(),
            timestamp,
            service,
            level,
            category,
            eventId,
            EmptyToNull(eventName),
            message,
            EmptyToNull(exceptionType),
            EmptyToNull(exceptionMessage),
            Guid.TryParse(competitionIdText, out var competitionId) ? competitionId : null,
            Guid.TryParse(runtimeInstanceIdText, out var runtimeInstanceId)
                ? runtimeInstanceId
                : null);
        return true;
    }

    private static bool TryReadEnum<TEnum>(
        IReadOnlyDictionary<string, string> values,
        string key,
        out TEnum value)
        where TEnum : struct, Enum
    {
        if (values.TryGetValue(key, out var text)
            && short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && Enum.IsDefined(typeof(TEnum), number))
        {
            value = (TEnum)Enum.ToObject(typeof(TEnum), number);
            return true;
        }
        value = default;
        return false;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
