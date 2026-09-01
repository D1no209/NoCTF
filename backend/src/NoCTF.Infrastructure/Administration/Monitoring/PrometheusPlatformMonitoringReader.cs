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
        var results = await Task.WhenAll(QueryDefinitions.Create(thresholds).Select(definition =>
            QueryAsync(client, definition, cancellationToken)));
        var values = results.ToDictionary(result => result.Kind);
        var prometheus = Result(values, PrometheusMeasurementKind.PrometheusAvailability);
        var prometheusAvailable = prometheus.Succeeded && prometheus.Value >= 1;
        if (!prometheusAvailable)
            logger.LogWarning("Prometheus is unavailable for platform monitoring.");

        return new(
            prometheusAvailable,
            timeProvider.GetUtcNow(),
            options.DashboardUri,
            Sample(values, PrometheusMeasurementKind.ApiRequestsPerSecond),
            Sample(values, PrometheusMeasurementKind.ApiP95Seconds),
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
            Sample(values, PrometheusMeasurementKind.LeaderboardProjectionP95Seconds),
            Sample(values, PrometheusMeasurementKind.LeaderboardPublishFailuresPerSecond),
            Sample(values, PrometheusMeasurementKind.LeaderboardSignalRPublishFailuresPerSecond),
            Sample(values, PrometheusMeasurementKind.RunnerOnlineCount),
            Sample(values, PrometheusMeasurementKind.RunnerMinimumAvailableRatio),
            Sample(values, PrometheusMeasurementKind.PostgreSqlConnectionUsageRatio),
            Sample(values, PrometheusMeasurementKind.RedisP99Seconds),
            Sample(values, PrometheusMeasurementKind.DiskAvailableRatio));
    }

    private async Task<PrometheusQueryResult> QueryAsync(
        HttpClient client,
        PrometheusQueryDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = $"api/v1/query?query={Uri.EscapeDataString(definition.Expression)}";
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
            var first = result[0];
            if (!first.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Array
                || value.GetArrayLength() < 2)
            {
                return new(definition.Kind, false, null);
            }

            var raw = value[1].GetString();
            if (string.Equals(raw, "NaN", StringComparison.Ordinal))
                return new(definition.Kind, true, null);
            if (!double.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed)
                || !double.IsFinite(parsed))
            {
                return new(definition.Kind, false, null);
            }

            return new(definition.Kind, true, parsed);
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
        DiskAvailableRatio
    }

    private sealed record PrometheusQueryDefinition(
        PrometheusMeasurementKind Kind,
        string Expression);

    private sealed record PrometheusQueryResult(
        PrometheusMeasurementKind Kind,
        bool Succeeded,
        double? Value);

    private static class QueryDefinitions
    {
        private const string CriticalStreams = "NOCTF_CONTROL|NOCTF_GAMEPLAY";

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
                    "sum(rate(noctf_api_requests_total{role=\"api\"}[5m])) or vector(0)"),
                new(PrometheusMeasurementKind.ApiP95Seconds,
                    "histogram_quantile(0.95, sum by (le) (rate(noctf_api_request_duration_seconds_bucket{role=\"api\"}[5m])))"),
                new(PrometheusMeasurementKind.ApiServerErrorRatio,
                    "(sum(rate(noctf_api_requests_total{role=\"api\",outcome=\"server_error\"}[5m])) or vector(0)) / clamp_min(sum(rate(noctf_api_requests_total{role=\"api\"}[5m])) or vector(0), 1)"),
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
                    "min(sum by (pool, resource) (noctf_runner_capacity_available{role=\"runner\"}) / clamp_min(sum by (pool, resource) (noctf_runner_capacity_total{role=\"runner\"}), 1)) or vector(0)"),
                new(PrometheusMeasurementKind.PostgreSqlConnectionUsageRatio,
                    "sum(pg_stat_activity_count) / clamp_min(max(pg_settings_max_connections), 1)"),
                new(PrometheusMeasurementKind.RedisP99Seconds,
                    "histogram_quantile(0.99, sum by (le) (rate(noctf_redis_operation_duration_seconds_bucket[5m])))"),
                new(PrometheusMeasurementKind.DiskAvailableRatio,
                    "min(node_filesystem_avail_bytes{fstype!~\"tmpfs|overlay\"} / node_filesystem_size_bytes{fstype!~\"tmpfs|overlay\"})")
            ];
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
            unavailable);
    }
}
