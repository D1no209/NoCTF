using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Observability;

public sealed class LokiPlatformLogReader(
    HttpClient client, TimeProvider clock, PlatformLogUserIdProtector userIds)
    : IPlatformLogReader
{
    private const int PageSize = 1_000;
    private const int MaximumQueryScan = 20_000;
    private const int MaximumExportScan = 700_000;

    public Task<PlatformLogQueryResult> QueryAsync(
        PlatformLogQuery query, CancellationToken ct) =>
        QueryCoreAsync(query, MaximumQueryScan, ct);

    public async Task<PlatformLogExportResult> ExportAsync(
        PlatformLogQuery query, CancellationToken ct)
    {
        var result = await QueryCoreAsync(query, MaximumExportScan, ct);
        if (result.State != PlatformLogReadState.Available)
            return new(result.State);
        var stream = new MemoryStream();
        foreach (var view in result.Items)
        {
            await JsonSerializer.SerializeAsync(stream, view,
                NoCtfWebMessageJsonContext.Default.PlatformLogView, ct);
            stream.WriteByte((byte)'\n');
        }
        stream.Position = 0;
        return new(PlatformLogReadState.Available,
            new PlatformLogExport(stream,
                $"platform-logs-{query.From!.Value:yyyyMMdd}-{query.To!.Value:yyyyMMdd}.jsonl"));
    }

    private async Task<PlatformLogQueryResult> QueryCoreAsync(
        PlatformLogQuery query, int maximumScanned, CancellationToken ct)
    {
        DateTimeOffset cursorAt = default;
        if (query.Cursor is not null && !TryParseCursor(query.Cursor, out cursorAt))
            return new(PlatformLogReadState.Unavailable, []);
        var now = clock.GetUtcNow();
        var from = query.From ?? now.AddDays(-14);
        var to = query.To ?? now;
        if (to < from || to - from > TimeSpan.FromDays(14))
            return new(PlatformLogReadState.Unavailable, []);
        var end = ToNanoseconds(to);
        if (query.Cursor is not null)
            end = Math.Min(end, ToNanoseconds(cursorAt));
        var start = ToNanoseconds(from);
        var items = new List<PlatformLogView>(query.Limit);
        var scanned = 0;
        string? lastCursor = null;
        try
        {
            while (end >= start && items.Count < query.Limit
                && scanned < maximumScanned)
            {
                var batch = await FetchAsync(start, end,
                    Math.Min(PageSize, maximumScanned - scanned), ct);
                if (batch.Count == 0) break;
                scanned += batch.Count;
                foreach (var row in batch.OrderByDescending(item => item.TimestampNanoseconds)
                    .ThenByDescending(item => item.View.Cursor, StringComparer.Ordinal))
                {
                    end = Math.Min(end, row.TimestampNanoseconds);
                    lastCursor = row.View.Cursor;
                    if (query.Cursor is not null
                        && row.TimestampNanoseconds == ToNanoseconds(cursorAt)
                        && string.CompareOrdinal(row.View.Cursor, query.Cursor) >= 0)
                        continue;
                    if (!Matches(row.View, query)) continue;
                    items.Add(row.View);
                    if (items.Count >= query.Limit) break;
                }
                if (items.Count >= query.Limit) break;
                end = batch[^1].TimestampNanoseconds - 1;
                if (batch.Count < PageSize) break;
            }
            return new(PlatformLogReadState.Available, items,
                items.Count >= query.Limit || scanned >= maximumScanned
                    ? lastCursor : null);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException or JsonException or InvalidOperationException
            or System.Security.Cryptography.CryptographicException or FormatException)
        {
            if (ct.IsCancellationRequested) throw;
            return new(PlatformLogReadState.Unavailable, []);
        }
    }

    private async Task<List<LokiRow>> FetchAsync(
        long start, long end, int limit, CancellationToken ct)
    {
        const string selector = "{service_name=~\"noctf-host-.*\"}";
        var path = "loki/api/v1/query_range?query=" + Uri.EscapeDataString(selector)
            + "&start=" + start.ToString(CultureInfo.InvariantCulture)
            + "&end=" + end.ToString(CultureInfo.InvariantCulture)
            + "&limit=" + limit.ToString(CultureInfo.InvariantCulture)
            + "&direction=BACKWARD";
        using var response = await client.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (root.GetProperty("status").GetString() != "success")
            throw new InvalidOperationException("Loki did not return a successful query.");
        var rows = new List<LokiRow>();
        foreach (var result in root.GetProperty("data").GetProperty("result").EnumerateArray())
        foreach (var value in result.GetProperty("values").EnumerateArray())
        {
            if (!long.TryParse(value[0].GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var timestamp))
                continue;
            var stored = JsonSerializer.Deserialize(value[1].GetString() ?? string.Empty,
                StoredPlatformLogJsonContext.Default.StoredPlatformLog);
            if (stored?.View is null || stored.View.UserId is not null)
                throw new InvalidOperationException("The Loki log document is invalid.");
            var view = stored.EncryptedUserId is null
                ? stored.View
                : stored.View with
                {
                    UserId = userIds.Unprotect(stored.EncryptedUserId)
                };
            rows.Add(new(timestamp, view));
        }
        return rows.OrderByDescending(item => item.TimestampNanoseconds).ToList();
    }

    private static bool Matches(PlatformLogView item, PlatformLogQuery query) =>
        item.Level >= query.MinimumLevel
        && (query.Service is null || item.Service == query.Service)
        && (query.From is null || item.Timestamp >= query.From)
        && (query.To is null || item.Timestamp <= query.To)
        && (query.Category is null || string.Equals(item.Category,
            query.Category, StringComparison.OrdinalIgnoreCase))
        && (query.Search is null || item.Category.Contains(query.Search,
                StringComparison.OrdinalIgnoreCase)
            || item.EventName?.Contains(query.Search,
                StringComparison.OrdinalIgnoreCase) == true
            || item.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
            || item.ExceptionType?.Contains(query.Search,
                StringComparison.OrdinalIgnoreCase) == true
            || item.ExceptionMessage?.Contains(query.Search,
                StringComparison.OrdinalIgnoreCase) == true)
        && (query.CompetitionId is null || item.CompetitionId == query.CompetitionId)
        && (query.RuntimeInstanceId is null || item.RuntimeInstanceId == query.RuntimeInstanceId)
        && (query.TeamId is null || item.TeamId == query.TeamId)
        && (query.UserId is null || item.UserId == query.UserId)
        && (query.CompetitionChallengeId is null
            || item.CompetitionChallengeId == query.CompetitionChallengeId)
        && (query.GameplayFactId is null || item.GameplayFactId == query.GameplayFactId);

    private static long ToNanoseconds(DateTimeOffset timestamp) =>
        checked((timestamp.UtcTicks - DateTime.UnixEpoch.Ticks) * 100);

    private static bool TryParseCursor(string cursor, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var separator = cursor.IndexOf(':');
        if (separator < 1 || cursor.Length != separator + 33
            || !Guid.TryParseExact(cursor[(separator + 1)..], "N", out _)
            || !long.TryParse(cursor[..separator], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ticks))
            return false;
        try
        {
            timestamp = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private sealed record LokiRow(long TimestampNanoseconds, PlatformLogView View);
}
