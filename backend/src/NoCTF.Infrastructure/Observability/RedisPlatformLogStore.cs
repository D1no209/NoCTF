using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Administration.PlatformLogs;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Observability;

public sealed record PlatformLogWriterOptions(
    PlatformLogService Service,
    int MaximumEntriesPerDay,
    int RetentionDays);

public static class PlatformLoggingRegistration
{
    public const int MaximumEntriesPerDay = 50_000;
    public const int RetentionDays = 14;

    public static IServiceCollection AddNoCtfPlatformLogging(
        this IServiceCollection services,
        IConfiguration _,
        PlatformLogService service)
    {
        services.AddSingleton(new PlatformLogWriterOptions(
            service,
            MaximumEntriesPerDay,
            RetentionDays));
        services.AddSingleton<ILoggerProvider, RedisPlatformLoggerProvider>();
        return services;
    }
}

public sealed class RedisPlatformLoggerProvider(
    IConnectionMultiplexer redis,
    PlatformLogWriterOptions options,
    TimeProvider? clock = null)
    : ILoggerProvider, ISupportExternalScope
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    public const string StreamKeyPrefix = "platform-logs:v2:";
    public const string Channel = "platform-logs:v1:live";
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) =>
        new RedisPlatformLogger(
            categoryName,
            redis,
            options,
            timeProvider,
            () => scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider provider) =>
        scopeProvider = provider;

    public void Dispose()
    {
    }

    private sealed class RedisPlatformLogger(
        string category,
        IConnectionMultiplexer redis,
        PlatformLogWriterOptions options,
        TimeProvider timeProvider,
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
            var timestamp = timeProvider.GetUtcNow();
            var message = Limit(
                PlatformLogRedactor.Redact(formatter(state, exception), properties),
                16_384);
            var exceptionMessage = exception is null
                ? null
                : Limit(PlatformLogRedactor.Redact(exception.ToString(), properties), 32_768);
            var competitionId = ReadGuid(properties, "CompetitionId");
            var runtimeInstanceId = ReadGuid(properties, "RuntimeInstanceId");
            var teamId = ReadGuid(properties, "TeamId");
            var userId = ReadGuid(properties, "UserId", "ActorUserId", "RelatedUserId");
            var competitionChallengeId = ReadGuid(properties, "CompetitionChallengeId");
            var gameplayFactId = ReadGuid(properties, "GameplayFactId");
            var level = ToPlatformLevel(logLevel);
            if (!redis.IsConnected)
                return;
            var database = redis.GetDatabase();
            try
            {
                var partition = DateOnly.FromDateTime(timestamp.UtcDateTime);
                var streamKey = StreamKeyFor(partition);
                var cursor = database.StreamAdd(
                    streamKey,
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
                        new("runtimeInstanceId", runtimeInstanceId?.ToString("D") ?? string.Empty),
                        new("teamId", teamId?.ToString("D") ?? string.Empty),
                        new("userId", userId?.ToString("D") ?? string.Empty),
                        new(
                            "competitionChallengeId",
                            competitionChallengeId?.ToString("D") ?? string.Empty),
                        new("gameplayFactId", gameplayFactId?.ToString("D") ?? string.Empty)
                    ],
                    maxLength: options.MaximumEntriesPerDay,
                    useApproximateMaxLength: false);
                var expiresAt = partition
                    .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                    .AddDays(options.RetentionDays);
                _ = database.KeyExpire(streamKey, expiresAt);
                var notification = new PlatformLogView(
                    ComposeCursor(partition, cursor.ToString()),
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
                    runtimeInstanceId,
                    teamId,
                    userId,
                    competitionChallengeId,
                    gameplayFactId);
                redis.GetSubscriber().Publish(
                    RedisChannel.Literal(Channel),
                    System.Text.Json.JsonSerializer.Serialize(notification,
                        NoCTF.Application.Messaging.NoCtfWebMessageJsonContext.Default.PlatformLogView));
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
            params string[] keys)
        {
            foreach (var property in properties)
            {
                if (!keys.Contains(property.Key, StringComparer.OrdinalIgnoreCase))
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

    public static string StreamKeyFor(DateOnly partition) =>
        $"{StreamKeyPrefix}{partition:yyyyMMdd}";

    internal static string ComposeCursor(DateOnly partition, string streamId) =>
        $"{partition:yyyyMMdd}:{streamId}";
}

public sealed class RedisPlatformLogStore(
    IConnectionMultiplexer redis,
    PlatformLogWriterOptions options,
    TimeProvider? clock = null) : IPlatformLogReader
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private const int ScanBatchSize = 500;
    private const int MaximumScannedEntries = 20_000;
    private const int MaximumExportScannedEntries = 700_000;
    private static readonly JsonSerializerOptions ExportJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PlatformLogQueryResult> QueryAsync(
        PlatformLogQuery query,
        CancellationToken ct) =>
        await QueryCoreAsync(query, MaximumScannedEntries, ct);

    public async Task<PlatformLogExportResult> ExportAsync(
        PlatformLogQuery query,
        CancellationToken ct)
    {
        var result = await QueryCoreAsync(query, MaximumExportScannedEntries, ct);
        if (result.State != PlatformLogReadState.Available)
            return new(result.State);
        var stream = new MemoryStream();
        foreach (var item in result.Items)
        {
            await JsonSerializer.SerializeAsync(stream, item, ExportJsonOptions, ct);
            stream.WriteByte((byte)'\n');
        }
        stream.Position = 0;
        return new(
            PlatformLogReadState.Available,
            new PlatformLogExport(
                stream,
                $"platform-logs-{query.From!.Value:yyyyMMdd}-{query.To!.Value:yyyyMMdd}.jsonl"));
    }

    private async Task<PlatformLogQueryResult> QueryCoreAsync(
        PlatformLogQuery query,
        int maximumScannedEntries,
        CancellationToken ct)
    {
        try
        {
            var database = redis.GetDatabase();
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var earliestRetained = today.AddDays(-(options.RetentionDays - 1));
            var fromPartition = query.From is null
                ? earliestRetained
                : DateOnly.FromDateTime(query.From.Value.UtcDateTime);
            if (fromPartition < earliestRetained)
                fromPartition = earliestRetained;
            var toPartition = query.To is null
                ? today
                : DateOnly.FromDateTime(query.To.Value.UtcDateTime);
            if (toPartition > today)
                toPartition = today;

            DateOnly? cursorPartition = null;
            string? cursorStreamId = null;
            if (query.Cursor is not null
                && !TryParseCursor(query.Cursor, out cursorPartition, out cursorStreamId))
                return new(PlatformLogReadState.Unavailable, []);
            if (cursorPartition is not null)
                toPartition = cursorPartition.Value;

            var items = new List<PlatformLogView>(query.Limit);
            var scanned = 0;
            string? lastCursor = null;
            var partition = toPartition;
            while (partition >= fromPartition
                && items.Count < query.Limit
                && scanned < maximumScannedEntries)
            {
                RedisValue minimumId = query.From is not null && partition == fromPartition
                    ? $"{query.From.Value.ToUnixTimeMilliseconds()}-0"
                    : "-";
                RedisValue maximumId = cursorPartition == partition && cursorStreamId is not null
                    ? $"({cursorStreamId}"
                    : query.To is not null && partition == toPartition
                        ? $"{query.To.Value.ToUnixTimeMilliseconds()}-999999999"
                        : "+";
                while (items.Count < query.Limit && scanned < maximumScannedEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    var entries = await database.StreamRangeAsync(
                        RedisPlatformLoggerProvider.StreamKeyFor(partition),
                        minimumId,
                        maximumId,
                        ScanBatchSize,
                        Order.Descending);
                    if (entries.Length == 0)
                        break;
                    scanned += entries.Length;
                    foreach (var entry in entries)
                    {
                        lastCursor = RedisPlatformLoggerProvider.ComposeCursor(
                            partition,
                            entry.Id.ToString());
                        if (TryMap(partition, entry, out var item) && Matches(item, query))
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
                cursorPartition = null;
                cursorStreamId = null;
                partition = partition.AddDays(-1);
            }
            return new(
                PlatformLogReadState.Available,
                items,
                items.Count == query.Limit || scanned >= maximumScannedEntries
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
        && (query.Category is null
            || string.Equals(item.Category, query.Category, StringComparison.OrdinalIgnoreCase))
        && (query.Search is null || MatchesSearch(item, query.Search))
        && (query.CompetitionId is null || item.CompetitionId == query.CompetitionId)
        && (query.RuntimeInstanceId is null
            || item.RuntimeInstanceId == query.RuntimeInstanceId)
        && (query.TeamId is null || item.TeamId == query.TeamId)
        && (query.UserId is null || item.UserId == query.UserId)
        && (query.CompetitionChallengeId is null
            || item.CompetitionChallengeId == query.CompetitionChallengeId)
        && (query.GameplayFactId is null || item.GameplayFactId == query.GameplayFactId);

    private static bool MatchesSearch(PlatformLogView item, string search) =>
        item.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.EventName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || item.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.ExceptionType?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || item.ExceptionMessage?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static bool TryMap(
        DateOnly partition,
        StreamEntry entry,
        out PlatformLogView item)
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
        values.TryGetValue("teamId", out var teamIdText);
        values.TryGetValue("userId", out var userIdText);
        values.TryGetValue("competitionChallengeId", out var competitionChallengeIdText);
        values.TryGetValue("gameplayFactId", out var gameplayFactIdText);
        item = new(
            RedisPlatformLoggerProvider.ComposeCursor(partition, entry.Id.ToString()),
            timestamp,
            service,
            level,
            category,
            eventId,
            EmptyToNull(eventName),
            PlatformLogRedactor.Redact(message, []),
            EmptyToNull(exceptionType),
            exceptionMessage is null
                ? null
                : PlatformLogRedactor.Redact(exceptionMessage, []),
            Guid.TryParse(competitionIdText, out var competitionId) ? competitionId : null,
            Guid.TryParse(runtimeInstanceIdText, out var runtimeInstanceId)
                ? runtimeInstanceId
                : null,
            Guid.TryParse(teamIdText, out var teamId) ? teamId : null,
            Guid.TryParse(userIdText, out var userId) ? userId : null,
            Guid.TryParse(competitionChallengeIdText, out var competitionChallengeId)
                ? competitionChallengeId
                : null,
            Guid.TryParse(gameplayFactIdText, out var gameplayFactId) ? gameplayFactId : null);
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

    private static bool TryParseCursor(
        string cursor,
        out DateOnly? partition,
        out string? streamId)
    {
        partition = null;
        streamId = null;
        var separator = cursor.IndexOf(':', StringComparison.Ordinal);
        if (separator != 8
            || !DateOnly.TryParseExact(
                cursor[..separator],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedPartition))
            return false;
        var candidate = cursor[(separator + 1)..];
        var idSeparator = candidate.IndexOf('-', StringComparison.Ordinal);
        if (idSeparator <= 0
            || !long.TryParse(candidate[..idSeparator], out _)
            || !long.TryParse(candidate[(idSeparator + 1)..], out _))
            return false;
        partition = parsedPartition;
        streamId = candidate;
        return true;
    }
}
