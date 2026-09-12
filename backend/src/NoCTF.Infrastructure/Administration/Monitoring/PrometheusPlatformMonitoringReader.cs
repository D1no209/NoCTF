using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Administration.Monitoring;

namespace NoCTF.Infrastructure.Administration.Monitoring;

internal sealed record PlatformMonitoringOptions(Uri? DashboardUri);

internal sealed class NoOpPlatformMonitoringReader(TimeProvider timeProvider)
    : IPlatformMonitoringReader
{
    public Task<PlatformMonitoringMeasurements> ReadAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(EmptyPlatformMonitoringMeasurements.Create(timeProvider.GetUtcNow()));
}

internal sealed class UnavailablePlatformMonitoringReader(
    TimeProvider timeProvider,
    PlatformMonitoringOptions options) : IPlatformMonitoringReader
{
    public Task<PlatformMonitoringMeasurements> ReadAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(EmptyPlatformMonitoringMeasurements.Create(
            timeProvider.GetUtcNow(),
            options.DashboardUri));
}

internal sealed class PrometheusPlatformMonitoringReader(
    IHttpClientFactory clients,
    TimeProvider timeProvider,
    PlatformMonitoringOptions options,
    PlatformMonitoringThresholds thresholds,
    ILogger<PrometheusPlatformMonitoringReader> logger) : IPlatformMonitoringReader
{
    public const string ClientName = "PlatformMonitoringPrometheus";

    public async Task<PlatformMonitoringMeasurements> ReadAsync(
        CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(ClientName);
        var capturedAt = timeProvider.GetUtcNow();
        var results = await Task.WhenAll(QueryDefinitions.Create(thresholds).Select(definition =>
            QueryAsync(client, definition, capturedAt, cancellationToken)));
        var values = results.ToDictionary(result => result.Kind);
        var prometheus = Result(values, PrometheusMeasurementKind.PrometheusAvailability);
        var prometheusAvailable = prometheus.Succeeded && prometheus.Value >= 1;
        if (!prometheusAvailable)
            logger.LogWarning("Prometheus is unavailable for platform monitoring.");

        return new(
            prometheusAvailable,
            capturedAt,
            options.DashboardUri,
            Sample(values, PrometheusMeasurementKind.ApiRequestsPerSecond),
            LatencySample(values, PrometheusMeasurementKind.ApiP95Seconds, PrometheusMeasurementKind.ApiSamples, PrometheusMeasurementKind.ApiSustainedSeconds),
            Sample(values, PrometheusMeasurementKind.ApiServerErrorRatio),
            Sample(values, PrometheusMeasurementKind.SignalRConnections),
            Sample(values, PrometheusMeasurementKind.NatsAvailability),
            Sample(values, PrometheusMeasurementKind.JetStreamStorageUsageRatio),
            Sample(values,
                PrometheusMeasurementKind.CriticalQueuePendingCount,
                PrometheusMeasurementKind.CriticalQueuePendingSustainedCount),
            Sample(values,
                PrometheusMeasurementKind.CriticalQueueAckPendingCount,
                PrometheusMeasurementKind.CriticalQueueAckPendingSustainedCount),
            Sample(values,
                PrometheusMeasurementKind.CriticalQueueRedeliveredCount,
                PrometheusMeasurementKind.CriticalQueueRedeliveredSustainedCount),
            Sample(values,
                PrometheusMeasurementKind.WolverineOutboxCount,
                PrometheusMeasurementKind.WolverineOutboxSustainedCount),
            Sample(values,
                PrometheusMeasurementKind.WolverineInboxCount,
                PrometheusMeasurementKind.WolverineInboxSustainedCount),
            Sample(values, PrometheusMeasurementKind.RuntimeWaitingCount),
            Sample(values, PrometheusMeasurementKind.RuntimeOldestWaitingSeconds),
            Sample(values, PrometheusMeasurementKind.LeaderboardMergeDispatchFailuresPerSecond),
            Sample(values, PrometheusMeasurementKind.LeaderboardCacheMissRebuildFailuresPerSecond),
            LatencySample(values, PrometheusMeasurementKind.LeaderboardProjectionP95Seconds, PrometheusMeasurementKind.ProjectionSamples),
            Sample(values, PrometheusMeasurementKind.LeaderboardPublishFailuresPerSecond),
            Sample(values, PrometheusMeasurementKind.LeaderboardSignalRPublishFailuresPerSecond),
            Sample(values, PrometheusMeasurementKind.RunnerOnlineCount),
            Sample(values, PrometheusMeasurementKind.RunnerMinimumAvailableRatio),
            Sample(values, PrometheusMeasurementKind.PostgreSqlConnectionUsageRatio),
            LatencySample(values, PrometheusMeasurementKind.RedisP99Seconds, PrometheusMeasurementKind.RedisSamples, PrometheusMeasurementKind.RedisSustainedSeconds),
            Sample(values, PrometheusMeasurementKind.DiskAvailableRatio),
            WindowSample(values, PrometheusMeasurementKind.FlagSubmissionsPerSecond),
            WindowSample(values, PrometheusMeasurementKind.FlagSubmissionsLastFiveMinutes),
            WindowSample(values, PrometheusMeasurementKind.FixSubmissionsPerSecond),
            WindowSample(values, PrometheusMeasurementKind.FixSubmissionsLastFiveMinutes),
            RatioSample(
                values,
                PrometheusMeasurementKind.FlagCorrectRatio,
                PrometheusMeasurementKind.FlagCompletedSamples),
            LatencySample(
                values,
                PrometheusMeasurementKind.FlagProcessingP95Seconds,
                PrometheusMeasurementKind.FlagProcessingSamples,
                minimumSamples: thresholds.GameplayMinimumSamples),
            RatioSample(
                values,
                PrometheusMeasurementKind.FlagPlatformErrorRatio,
                PrometheusMeasurementKind.FlagProcessingSamples),
            BuildLatencyDetails(values), BuildPoolResources(values));
    }

    private async Task<PrometheusQueryResult> QueryAsync(
        HttpClient client,
        PrometheusQueryDefinition definition,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = $"api/v1/query?query={Uri.EscapeDataString(definition.Expression)}&time={capturedAt.ToUnixTimeSeconds()}";
            using var response = await client.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(definition.Kind, false, null);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("status", out var status)
                || status.GetString() != "success"
                || !document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Array)
            {
                return new(definition.Kind, false, null);
            }

            if (result.GetArrayLength() == 0)
                return new(definition.Kind, true, null);
            var rows = new List<PrometheusRow>();
            foreach (var first in result.EnumerateArray())
            {
            if (!first.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Array
                || value.GetArrayLength() < 2)
            {
                return new(definition.Kind, false, null);
            }

            var raw = value[1].GetString();
            if (string.Equals(raw, "NaN", StringComparison.Ordinal))
            {
                rows.Add(new(Labels(first), null));
                continue;
            }
            if (!double.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed)
                || !double.IsFinite(parsed))
            {
                return new(definition.Kind, false, null);
            }

                rows.Add(new(Labels(first), parsed));
            }
            return new(definition.Kind, true, rows.FirstOrDefault()?.Value, rows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or OperationCanceledException
            or JsonException)
        {
            logger.LogDebug(exception,
                "Prometheus query failed for monitoring measurement {Measurement}.",
                definition.Kind);
            return new(definition.Kind, false, null);
        }
    }

    private static IReadOnlyDictionary<string, string> Labels(JsonElement row) =>
        row.TryGetProperty("metric", out var labels) && labels.ValueKind == JsonValueKind.Object
            ? labels.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "")
            : new Dictionary<string, string>();

    private PlatformMonitoringSample LatencySample(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values,
        PrometheusMeasurementKind kind, PrometheusMeasurementKind countKind,
        PrometheusMeasurementKind? sustainedKind = null, string? label = null,
        string? key = null, int? minimumSamples = null)
    {
        var count = Read(values, countKind, label, key);
        var sample = Read(values, kind, label, key);
        var sustained = sustainedKind.HasValue ? Read(values, sustainedKind.Value, label, key) : sample;
        if (count.State == PlatformMonitoringSampleState.Unavailable || sustained.State == PlatformMonitoringSampleState.Unavailable)
            sample = sample with { State = PlatformMonitoringSampleState.Unavailable };
        else if ((count.Value ?? 0) <= 0)
            sample = PlatformMonitoringSample.NoSamples();
        return sample with
        {
            SampleCount = count.State == PlatformMonitoringSampleState.Unavailable ? null : count.Value ?? 0,
            MinimumSamples = minimumSamples ?? thresholds.LatencyMinimumSamples,
            WindowSeconds = 300,
            // Missing sustained evidence means not yet eligible, never fall back to the current spike.
            ThresholdValue = sustainedKind.HasValue ? sustained.Value ?? 0 : null
        };
    }

    private PlatformMonitoringSample RatioSample(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values,
        PrometheusMeasurementKind ratioKind,
        PrometheusMeasurementKind countKind)
    {
        var count = Sample(values, countKind);
        var ratio = Sample(values, ratioKind);
        if (count.State == PlatformMonitoringSampleState.Unavailable
            || ratio.State == PlatformMonitoringSampleState.Unavailable)
            ratio = ratio with { State = PlatformMonitoringSampleState.Unavailable };
        else if ((count.Value ?? 0) <= 0)
            ratio = PlatformMonitoringSample.NoSamples();
        return ratio with
        {
            SampleCount = count.State == PlatformMonitoringSampleState.Unavailable
                ? null
                : count.Value ?? 0,
            MinimumSamples = thresholds.GameplayMinimumSamples,
            WindowSeconds = 300
        };
    }

    private static PlatformMonitoringSample Read(IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values,
        PrometheusMeasurementKind kind, string? label, string? key)
    {
        if (label is null) return Sample(values, kind);
        var result = Result(values, kind);
        if (!result.Succeeded) return PlatformMonitoringSample.Unavailable();
        var value = result.Rows?.FirstOrDefault(row => row.Labels.GetValueOrDefault(label) == key)?.Value;
        return value.HasValue ? PlatformMonitoringSample.From(value.Value) : PlatformMonitoringSample.NoSamples();
    }

    private IReadOnlyList<PlatformMonitoringLatencyMeasurement> BuildLatencyDetails(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values)
    {
        var rows = new List<PlatformMonitoringLatencyMeasurement>();
        foreach (var (key, kind) in new[] { ("rest", PlatformMonitoringLatencyKind.Rest), ("signalr", PlatformMonitoringLatencyKind.SignalR),
            ("upload", PlatformMonitoringLatencyKind.Upload), ("download", PlatformMonitoringLatencyKind.Download) })
            rows.Add(Row(kind, key, "request_kind", false));
        foreach (var key in (Result(values, PrometheusMeasurementKind.RedisDetailSamples).Rows ?? [])
            .Select(row => row.Labels.GetValueOrDefault("endpoint")).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().Order())
            rows.Add(Row(PlatformMonitoringLatencyKind.RedisOperation, key!, "endpoint", true));
        return rows;

        PlatformMonitoringLatencyMeasurement Row(PlatformMonitoringLatencyKind kind, string key, string label, bool redis) => new(
            kind, redis ? key : "",
            LatencySample(values, redis ? PrometheusMeasurementKind.RedisDetailP95 : PrometheusMeasurementKind.HttpP95,
                redis ? PrometheusMeasurementKind.RedisDetailSamples : PrometheusMeasurementKind.HttpSamples,
                redis ? null : PrometheusMeasurementKind.HttpSustained, label, key),
            LatencySample(values, redis ? PrometheusMeasurementKind.RedisDetailP99 : PrometheusMeasurementKind.HttpP99,
                redis ? PrometheusMeasurementKind.RedisDetailSamples : PrometheusMeasurementKind.HttpSamples,
                redis ? PrometheusMeasurementKind.RedisDetailSustained : null, label, key),
            Read(values, redis ? PrometheusMeasurementKind.RedisDetailMean : PrometheusMeasurementKind.HttpMean, label, key),
            Read(values, redis ? PrometheusMeasurementKind.RedisDetailRate : PrometheusMeasurementKind.HttpRate, label, key),
            Read(values, redis ? PrometheusMeasurementKind.RedisDetailErrors : PrometheusMeasurementKind.HttpErrors, label, key));
    }

    private static IReadOnlyList<PlatformMonitoringPoolResource> BuildPoolResources(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values) =>
        (Result(values, PrometheusMeasurementKind.PoolTotal).Rows ?? []).Select(row =>
        {
            var pool = row.Labels.GetValueOrDefault("pool") ?? "";
            var resource = row.Labels.GetValueOrDefault("resource");
            PlatformMonitoringResource? kind = resource switch
            {
                "memory" => PlatformMonitoringResource.Memory, "cpu" => PlatformMonitoringResource.Cpu,
                "pids" => PlatformMonitoringResource.Pids, _ => null
            };
            if (kind is null) return null;
            var available = Result(values, PrometheusMeasurementKind.PoolAvailable).Rows?.FirstOrDefault(candidate =>
                candidate.Labels.GetValueOrDefault("pool") == pool && candidate.Labels.GetValueOrDefault("resource") == resource)?.Value;
            return new PlatformMonitoringPoolResource(pool, kind.Value, available, row.Value,
                Read(values, PrometheusMeasurementKind.PoolOnline, "pool", pool).Value);
        }).OfType<PlatformMonitoringPoolResource>().OrderBy(row => row.Pool).ThenBy(row => row.Resource).ToArray();

    private static PlatformMonitoringSample Sample(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values,
        PrometheusMeasurementKind kind,
        PrometheusMeasurementKind? thresholdKind = null)
    {
        var result = Result(values, kind);
        if (!result.Succeeded)
            return new(result.Value, PlatformMonitoringSampleState.Unavailable);
        if (result.Value is null)
            return PlatformMonitoringSample.NoSamples();
        if (thresholdKind is null)
            return PlatformMonitoringSample.From(result.Value.Value);

        var thresholdResult = Result(values, thresholdKind.Value);
        if (!thresholdResult.Succeeded || thresholdResult.Value is null)
        {
            return new(
                result.Value,
                PlatformMonitoringSampleState.Unavailable,
                thresholdResult.Value);
        }
        return PlatformMonitoringSample.From(result.Value.Value, thresholdResult.Value.Value);
    }

    private static PlatformMonitoringSample WindowSample(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values,
        PrometheusMeasurementKind kind) =>
        Sample(values, kind) with { WindowSeconds = 300 };

    private static PrometheusQueryResult Result(
        IReadOnlyDictionary<PrometheusMeasurementKind, PrometheusQueryResult> values,
        PrometheusMeasurementKind kind) =>
        values.TryGetValue(kind, out var result)
            ? result
            : new(kind, false, null);

    private enum PrometheusMeasurementKind : short
    {
        PrometheusAvailability,
        ApiRequestsPerSecond,
        ApiP95Seconds,
        ApiServerErrorRatio,
        SignalRConnections,
        NatsAvailability,
        JetStreamStorageUsageRatio,
        CriticalQueuePendingCount,
        CriticalQueuePendingSustainedCount,
        CriticalQueueAckPendingCount,
        CriticalQueueAckPendingSustainedCount,
        CriticalQueueRedeliveredCount,
        CriticalQueueRedeliveredSustainedCount,
        WolverineOutboxCount,
        WolverineOutboxSustainedCount,
        WolverineInboxCount,
        WolverineInboxSustainedCount,
        RuntimeWaitingCount,
        RuntimeOldestWaitingSeconds,
        LeaderboardMergeDispatchFailuresPerSecond,
        LeaderboardCacheMissRebuildFailuresPerSecond,
        LeaderboardProjectionP95Seconds,
        LeaderboardPublishFailuresPerSecond,
        LeaderboardSignalRPublishFailuresPerSecond,
        RunnerOnlineCount,
        RunnerMinimumAvailableRatio,
        PostgreSqlConnectionUsageRatio,
        RedisP99Seconds,
        DiskAvailableRatio,
        ApiSamples, ApiSustainedSeconds, RedisSamples, RedisSustainedSeconds, ProjectionSamples,
        HttpSamples, HttpP95, HttpP99, HttpMean, HttpRate, HttpErrors, HttpSustained,
        RedisDetailSamples, RedisDetailP95, RedisDetailP99, RedisDetailMean, RedisDetailRate, RedisDetailErrors, RedisDetailSustained,
        PoolAvailable, PoolTotal, PoolOnline,
        FlagSubmissionsPerSecond, FlagSubmissionsLastFiveMinutes,
        FixSubmissionsPerSecond, FixSubmissionsLastFiveMinutes,
        FlagCorrectRatio, FlagCompletedSamples,
        FlagProcessingP95Seconds, FlagProcessingSamples,
        FlagPlatformErrorRatio
    }

    private sealed record PrometheusQueryDefinition(
        PrometheusMeasurementKind Kind,
        string Expression);

    private sealed record PrometheusQueryResult(
        PrometheusMeasurementKind Kind,
        bool Succeeded,
        double? Value, IReadOnlyList<PrometheusRow>? Rows = null);

    private sealed record PrometheusRow(IReadOnlyDictionary<string, string> Labels, double? Value);

    private static class QueryDefinitions
    {
        private const string CriticalStreams = "NOCTF_CONTROL|NOCTF_GAMEPLAY";
        private const string Rest = "{role=\"api\",request_kind=\"rest\"}";
        private const string ApiHistogram = "noctf_api_request_duration_seconds";
        private const string RedisHistogram = "noctf_redis_operation_duration_seconds";
        private const string FlagSubmissions =
            "noctf_gameplay_fact_submissions_total{role=\"api\",kind=~\"flag|break\"}";
        private const string FixSubmissions =
            "noctf_gameplay_fact_submissions_total{role=\"api\",kind=\"fix\"}";
        private const string FlagProcessingSelector =
            "{role=\"worker\",kind=~\"flag|break\"}";
        private const string FlagCompletedSelector =
            "{role=\"worker\",kind=~\"flag|break\",outcome=~\"correct|incorrect\"}";
        private const string FlagCorrectSelector =
            "{role=\"worker\",kind=~\"flag|break\",outcome=\"correct\"}";
        private const string FlagPlatformErrorSelector =
            "{role=\"worker\",kind=~\"flag|break\",outcome=\"platform_error\"}";
        private const string FlagProcessingCounter =
            "noctf_gameplay_fact_processing_total";
        private const string FlagProcessingHistogram =
            "noctf_gameplay_fact_processing_duration_seconds";

        public static IReadOnlyList<PrometheusQueryDefinition> Create(
            PlatformMonitoringThresholds thresholds)
        {
            var pending = CriticalConsumerSum("jetstream_consumer_num_pending");
            var ackPending = CriticalConsumerSum("jetstream_consumer_num_ack_pending");
            var redelivered = CriticalConsumerSum("jetstream_consumer_num_redelivered");
            const string outbox = "sum(wolverine_outbox_count_Messages)";
            const string inbox = "sum(wolverine_inbox_count_Messages)";
            var window = $"{thresholds.SustainedWindowMinutes}m";

            return
            [
                new(PrometheusMeasurementKind.PrometheusAvailability, "vector(1)"),
                new(PrometheusMeasurementKind.ApiRequestsPerSecond,
                    $"sum(rate(noctf_api_requests_total{Rest}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.ApiP95Seconds,
                    Quantile(ApiHistogram, Rest, "", "0.95")),
                new(PrometheusMeasurementKind.ApiServerErrorRatio,
                    "((sum(rate(noctf_api_requests_total{role=\"api\",request_kind=\"rest\",outcome=\"server_error\"}[5m])) or vector(0)) / (sum(rate(noctf_api_requests_total{role=\"api\",request_kind=\"rest\"}[5m])) > 0)) or vector(0)"),
                new(PrometheusMeasurementKind.SignalRConnections,
                    "sum(noctf_signalr_connections{role=\"api\"}) or vector(0)"),
                new(PrometheusMeasurementKind.NatsAvailability,
                    "max(up{job=\"nats\"}) or vector(0)"),
                new(PrometheusMeasurementKind.JetStreamStorageUsageRatio,
                    "sum(max by (server_name) (jetstream_server_total_message_bytes)) / clamp_min(sum(max by (server_name) (jetstream_server_max_storage)), 1)"),
                new(PrometheusMeasurementKind.CriticalQueuePendingCount, pending),
                new(PrometheusMeasurementKind.CriticalQueuePendingSustainedCount,
                    Sustained(pending, window)),
                new(PrometheusMeasurementKind.CriticalQueueAckPendingCount, ackPending),
                new(PrometheusMeasurementKind.CriticalQueueAckPendingSustainedCount,
                    Sustained(ackPending, window)),
                new(PrometheusMeasurementKind.CriticalQueueRedeliveredCount, redelivered),
                new(PrometheusMeasurementKind.CriticalQueueRedeliveredSustainedCount,
                    Sustained(redelivered, window)),
                new(PrometheusMeasurementKind.WolverineOutboxCount, outbox),
                new(PrometheusMeasurementKind.WolverineOutboxSustainedCount,
                    Sustained(outbox, window)),
                new(PrometheusMeasurementKind.WolverineInboxCount, inbox),
                new(PrometheusMeasurementKind.WolverineInboxSustainedCount,
                    Sustained(inbox, window)),
                new(PrometheusMeasurementKind.RuntimeWaitingCount,
                    "sum(noctf_runtime_waiting{role=\"worker\"}) or vector(0)"),
                new(PrometheusMeasurementKind.RuntimeOldestWaitingSeconds,
                    "max(noctf_runtime_waiting_oldest_age_seconds{role=\"worker\"}) or vector(0)"),
                new(PrometheusMeasurementKind.LeaderboardMergeDispatchFailuresPerSecond,
                    "sum(rate(noctf_leaderboard_merge_dispatches_total{role=\"worker\",outcome=\"failure\"}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.LeaderboardCacheMissRebuildFailuresPerSecond,
                    "sum(rate(noctf_leaderboard_cache_miss_rebuilds_total{role=\"api\",outcome=\"failure\"}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.LeaderboardProjectionP95Seconds,
                    "histogram_quantile(0.95, sum by (le) (rate(noctf_leaderboard_projection_duration_seconds_bucket{role=\"worker\"}[5m])))"),
                new(PrometheusMeasurementKind.LeaderboardPublishFailuresPerSecond,
                    "sum(rate(noctf_leaderboard_publish_failures_total{role=\"worker\"}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.LeaderboardSignalRPublishFailuresPerSecond,
                    "sum(rate(noctf_signalr_publishes_total{role=\"api\",endpoint=\"leaderboard\",outcome=\"failure\"}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.RunnerOnlineCount,
                    "sum(noctf_runner_online{role=\"runner\"}) or vector(0)"),
                new(PrometheusMeasurementKind.RunnerMinimumAvailableRatio,
                    "min(sum by (pool, resource) (noctf_runner_capacity_available{role=\"runner\"}) / (sum by (pool, resource) (noctf_runner_capacity_total{role=\"runner\"}) > 0))"),
                new(PrometheusMeasurementKind.PostgreSqlConnectionUsageRatio,
                    "sum(pg_stat_activity_count) / clamp_min(max(pg_settings_max_connections), 1)"),
                new(PrometheusMeasurementKind.RedisP99Seconds,
                    Quantile(RedisHistogram, "", "", "0.99")),
                new(PrometheusMeasurementKind.DiskAvailableRatio,
                    "min(node_filesystem_avail_bytes{fstype!~\"tmpfs|overlay\"} / node_filesystem_size_bytes{fstype!~\"tmpfs|overlay\"})"),
                new(PrometheusMeasurementKind.FlagSubmissionsPerSecond,
                    $"sum(rate({FlagSubmissions}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.FlagSubmissionsLastFiveMinutes,
                    $"sum(increase({FlagSubmissions}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.FixSubmissionsPerSecond,
                    $"sum(rate({FixSubmissions}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.FixSubmissionsLastFiveMinutes,
                    $"sum(increase({FixSubmissions}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.FlagCorrectRatio,
                    Ratio(FlagProcessingCounter, FlagCorrectSelector, FlagCompletedSelector)),
                new(PrometheusMeasurementKind.FlagCompletedSamples,
                    $"sum(increase({FlagProcessingCounter}{FlagCompletedSelector}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.FlagProcessingP95Seconds,
                    Quantile(FlagProcessingHistogram, FlagProcessingSelector, "", "0.95")),
                new(PrometheusMeasurementKind.FlagProcessingSamples,
                    Count(FlagProcessingHistogram, FlagProcessingSelector, "")),
                new(PrometheusMeasurementKind.FlagPlatformErrorRatio,
                    Ratio(FlagProcessingCounter, FlagPlatformErrorSelector, FlagProcessingSelector)),
                new(PrometheusMeasurementKind.ApiSamples, Count(ApiHistogram, Rest, "")),
                new(PrometheusMeasurementKind.ApiSustainedSeconds,
                    SustainedLatency(Quantile(ApiHistogram, Rest, "", "0.95"), Count(ApiHistogram, Rest, ""), thresholds)),
                new(PrometheusMeasurementKind.RedisSamples, Count(RedisHistogram, "", "")),
                new(PrometheusMeasurementKind.RedisSustainedSeconds,
                    SustainedLatency(Quantile(RedisHistogram, "", "", "0.99"), Count(RedisHistogram, "", ""), thresholds)),
                new(PrometheusMeasurementKind.ProjectionSamples, Count("noctf_leaderboard_projection_duration_seconds", "{role=\"worker\"}", "")),
                .. DetailQueries(false, thresholds), .. DetailQueries(true, thresholds),
                new(PrometheusMeasurementKind.PoolAvailable, "sum by (pool,resource) (noctf_runner_capacity_available{role=\"runner\"})"),
                new(PrometheusMeasurementKind.PoolTotal, "sum by (pool,resource) (noctf_runner_capacity_total{role=\"runner\"})"),
                new(PrometheusMeasurementKind.PoolOnline, "sum by (pool) (noctf_runner_online{role=\"runner\"})")
            ];
        }

        private static IEnumerable<PrometheusQueryDefinition> DetailQueries(bool redis, PlatformMonitoringThresholds thresholds)
        {
            var histogram = redis ? RedisHistogram : ApiHistogram;
            var selector = redis ? "" : "{role=\"api\",request_kind=~\"rest|signalr|upload|download\"}";
            var group = redis ? "endpoint" : "request_kind";
            var count = Count(histogram, selector, group);
            var rate = Sum(group, $"rate({histogram}_count{selector}[5m])");
            var errors = redis ? "{outcome!=\"success\"}" : "{role=\"api\",request_kind=~\"rest|signalr|upload|download\",outcome=\"server_error\"}";
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailSamples : PrometheusMeasurementKind.HttpSamples, count);
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailP95 : PrometheusMeasurementKind.HttpP95, Quantile(histogram, selector, group, "0.95"));
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailP99 : PrometheusMeasurementKind.HttpP99, Quantile(histogram, selector, group, "0.99"));
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailMean : PrometheusMeasurementKind.HttpMean,
                $"{Sum(group, $"rate({histogram}_sum{selector}[5m])")} / ({rate} > 0)");
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailRate : PrometheusMeasurementKind.HttpRate, rate);
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailErrors : PrometheusMeasurementKind.HttpErrors,
                $"({Sum(group, $"rate({histogram}_count{errors}[5m])")} or ({rate}) * 0) / ({rate} > 0)");
            yield return new(redis ? PrometheusMeasurementKind.RedisDetailSustained : PrometheusMeasurementKind.HttpSustained,
                SustainedLatency(Quantile(histogram, selector, group, redis ? "0.99" : "0.95"), count, thresholds));
        }

        private static string Sum(string group, string expression) =>
            group.Length == 0 ? $"sum({expression})" : $"sum by ({group}) ({expression})";
        private static string Count(string histogram, string selector, string group) => Sum(group, $"increase({histogram}_count{selector}[5m])");
        private static string Quantile(string histogram, string selector, string group, string quantile) =>
            $"histogram_quantile({quantile}, sum by (le{(group.Length == 0 ? "" : "," + group)}) (rate({histogram}_bucket{selector}[5m])))";
        private static string Ratio(string counter, string numeratorSelector, string denominatorSelector) =>
            $"sum(increase({counter}{numeratorSelector}[5m])) / (sum(increase({counter}{denominatorSelector}[5m])) > 0)";
        private static string SustainedLatency(string quantile, string samples, PlatformMonitoringThresholds thresholds)
        {
            var window = $"{thresholds.LatencySustainedWindowMinutes}m";
            // A sparse/missing point is not proof of a sustained breach. Keep the same five-minute
            // distribution at each evaluation; the subquery is an alert hold, not a wider sample window.
            var eligible = $"((({quantile}) >= 0) * (({samples}) >= bool {thresholds.LatencyMinimumSamples})) or (({samples}) * 0)";
            return $"min_over_time(({eligible})[{window}:15s]) and (count_over_time(({samples})[{window}:15s]) >= {thresholds.LatencySustainedWindowMinutes * 4})";
        }

        private static string CriticalConsumerSum(string metric) =>
            $"sum(max by (stream_name, consumer_name) ({metric}{{is_consumer_leader=\"true\",stream_name=~\"{CriticalStreams}\"}})) or vector(0)";

        private static string Sustained(string expression, string window) =>
            $"min_over_time(({expression})[{window}:15s])";
    }
}

file static class EmptyPlatformMonitoringMeasurements
{
    public static PlatformMonitoringMeasurements Create(
        DateTimeOffset capturedAt,
        Uri? dashboardUri = null)
    {
        var unavailable = PlatformMonitoringSample.Unavailable();
        return new(
            false,
            capturedAt,
            dashboardUri,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable);
    }
}
