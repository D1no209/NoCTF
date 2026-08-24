using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Administration.Monitoring;

namespace NoCTF.Infrastructure.Administration.Monitoring;

internal sealed record PlatformMonitoringOptions(Uri? DashboardUri);

internal sealed class OpenApiPlatformMonitoringReader(TimeProvider timeProvider)
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
    ILogger<PrometheusPlatformMonitoringReader> logger) : IPlatformMonitoringReader
{
    public const string ClientName = "PlatformMonitoringPrometheus";

    public async Task<PlatformMonitoringMeasurements> ReadAsync(
        CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(ClientName);
        var results = await Task.WhenAll(QueryDefinitions.All.Select(definition =>
            QueryAsync(client, definition, cancellationToken)));
        var values = results.ToDictionary(result => result.Kind, result => result.Value);
        var sourceAvailable = results.Any(result => result.Succeeded);
        if (!sourceAvailable)
            logger.LogWarning("Platform monitoring data source is unavailable.");

        return new(
            sourceAvailable,
            timeProvider.GetUtcNow(),
            options.DashboardUri,
            Value(values, PrometheusMeasurementKind.ApiRequestsPerSecond),
            Value(values, PrometheusMeasurementKind.ApiP95Seconds),
            Value(values, PrometheusMeasurementKind.ApiServerErrorRatio),
            Value(values, PrometheusMeasurementKind.SignalRConnections),
            Value(values, PrometheusMeasurementKind.CriticalQueueDepth),
            Value(values, PrometheusMeasurementKind.CriticalQueueOldestSeconds),
            Value(values, PrometheusMeasurementKind.RuntimeWaitingCount),
            Value(values, PrometheusMeasurementKind.RuntimeOldestWaitingSeconds),
            Value(values, PrometheusMeasurementKind.LeaderboardInvalidationsPerSecond),
            Value(values, PrometheusMeasurementKind.LeaderboardMergeDispatchesPerSecond),
            Value(values, PrometheusMeasurementKind.LeaderboardCacheMissRebuildsPerSecond),
            Value(values, PrometheusMeasurementKind.LeaderboardProjectionP95Seconds),
            Value(values, PrometheusMeasurementKind.RunnerOnlineCount),
            Value(values, PrometheusMeasurementKind.RunnerMinimumAvailableRatio),
            Value(values, PrometheusMeasurementKind.PostgreSqlConnectionUsageRatio),
            Value(values, PrometheusMeasurementKind.RedisP99Seconds),
            Value(values, PrometheusMeasurementKind.DiskAvailableRatio));
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
            return double.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed)
                ? new(definition.Kind, true, parsed)
                : new(definition.Kind, false, null);
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

    private static double? Value(
        IReadOnlyDictionary<PrometheusMeasurementKind, double?> values,
        PrometheusMeasurementKind kind) =>
        values.TryGetValue(kind, out var value) ? value : null;

    private enum PrometheusMeasurementKind : short
    {
        ApiRequestsPerSecond,
        ApiP95Seconds,
        ApiServerErrorRatio,
        SignalRConnections,
        CriticalQueueDepth,
        CriticalQueueOldestSeconds,
        RuntimeWaitingCount,
        RuntimeOldestWaitingSeconds,
        LeaderboardInvalidationsPerSecond,
        LeaderboardMergeDispatchesPerSecond,
        LeaderboardCacheMissRebuildsPerSecond,
        LeaderboardProjectionP95Seconds,
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
        public static readonly IReadOnlyList<PrometheusQueryDefinition> All =
        [
            new(PrometheusMeasurementKind.ApiRequestsPerSecond,
                "sum(rate(noctf_api_requests_total{role=\"api\"}[5m]))"),
            new(PrometheusMeasurementKind.ApiP95Seconds,
                "histogram_quantile(0.95, sum by (le) (rate(noctf_api_request_duration_seconds_bucket{role=\"api\"}[5m])))"),
            new(PrometheusMeasurementKind.ApiServerErrorRatio,
                "sum(rate(noctf_api_requests_total{role=\"api\",outcome=\"server_error\"}[5m])) / clamp_min(sum(rate(noctf_api_requests_total{role=\"api\"}[5m])), 1)"),
            new(PrometheusMeasurementKind.SignalRConnections,
                "sum(noctf_signalr_connections{role=\"api\"})"),
            new(PrometheusMeasurementKind.CriticalQueueDepth,
                "sum(noctf_worker_queue_depth{role=\"worker\",queue=~\"noctf-control|noctf-gameplay\"})"),
            new(PrometheusMeasurementKind.CriticalQueueOldestSeconds,
                "max(noctf_worker_queue_oldest_age_seconds{role=\"worker\",queue=~\"noctf-control|noctf-gameplay\"})"),
            new(PrometheusMeasurementKind.RuntimeWaitingCount,
                "sum(noctf_runtime_waiting{role=\"worker\"})"),
            new(PrometheusMeasurementKind.RuntimeOldestWaitingSeconds,
                "max(noctf_runtime_waiting_oldest_age_seconds{role=\"worker\"})"),
            new(PrometheusMeasurementKind.LeaderboardInvalidationsPerSecond,
                "sum(rate(noctf_leaderboard_cache_invalidations_total{role=\"worker\"}[5m]))"),
            new(PrometheusMeasurementKind.LeaderboardMergeDispatchesPerSecond,
                "sum(rate(noctf_leaderboard_merge_dispatches_total{role=\"worker\"}[5m]))"),
            new(PrometheusMeasurementKind.LeaderboardCacheMissRebuildsPerSecond,
                "sum(rate(noctf_leaderboard_cache_miss_rebuilds_total{role=\"api\"}[5m]))"),
            new(PrometheusMeasurementKind.LeaderboardProjectionP95Seconds,
                "histogram_quantile(0.95, sum by (le) (rate(noctf_leaderboard_projection_duration_seconds_bucket{role=\"worker\"}[5m])))"),
            new(PrometheusMeasurementKind.RunnerOnlineCount,
                "sum(noctf_runner_online{role=\"runner\"})"),
            new(PrometheusMeasurementKind.RunnerMinimumAvailableRatio,
                "min(sum by (pool, resource) (noctf_runner_capacity_available{role=\"runner\"}) / clamp_min(sum by (pool, resource) (noctf_runner_capacity_total{role=\"runner\"}), 1))"),
            new(PrometheusMeasurementKind.PostgreSqlConnectionUsageRatio,
                "sum(pg_stat_activity_count) / clamp_min(max(pg_settings_max_connections), 1)"),
            new(PrometheusMeasurementKind.RedisP99Seconds,
                "histogram_quantile(0.99, sum by (le) (rate(noctf_redis_operation_duration_seconds_bucket[5m])))"),
            new(PrometheusMeasurementKind.DiskAvailableRatio,
                "min(node_filesystem_avail_bytes{fstype!~\"tmpfs|overlay\"} / node_filesystem_size_bytes{fstype!~\"tmpfs|overlay\"})")
        ];
    }
}

file static class EmptyPlatformMonitoringMeasurements
{
    public static PlatformMonitoringMeasurements Create(
        DateTimeOffset capturedAt,
        Uri? dashboardUri = null) =>
        new(false, capturedAt, dashboardUri,
            null, null, null, null, null,
            null, null, null, null, null,
            null, null, null, null, null,
            null, null);
}
