namespace NoCTF.Application.Administration.Monitoring;

public enum PlatformMonitoringStatus : short
{
    Healthy,
    Warning,
    Critical,
    Unavailable
}

public enum PlatformMonitoringMetricKind : short
{
    ApiRequestsPerSecond,
    ApiP95Milliseconds,
    ApiServerErrorPercent,
    SignalRConnections,
    CriticalQueueDepth,
    CriticalQueueOldestSeconds,
    RuntimeWaitingCount,
    RuntimeOldestWaitingSeconds,
    LeaderboardInvalidationsPerSecond,
    LeaderboardMergeDispatchesPerSecond,
    LeaderboardCacheMissRebuildsPerSecond,
    LeaderboardProjectionP95Milliseconds,
    RunnerOnlineCount,
    RunnerMinimumAvailablePercent,
    PostgreSqlConnectionUsagePercent,
    RedisP99Milliseconds,
    DiskAvailablePercent
}

public enum PlatformMonitoringUnit : short
{
    Count,
    PerSecond,
    Milliseconds,
    Seconds,
    Percent
}

public sealed record PlatformMonitoringMeasurements(
    bool SourceAvailable,
    DateTimeOffset CapturedAt,
    Uri? DashboardUri,
    double? ApiRequestsPerSecond,
    double? ApiP95Seconds,
    double? ApiServerErrorRatio,
    double? SignalRConnections,
    double? CriticalQueueDepth,
    double? CriticalQueueOldestSeconds,
    double? RuntimeWaitingCount,
    double? RuntimeOldestWaitingSeconds,
    double? LeaderboardInvalidationsPerSecond,
    double? LeaderboardMergeDispatchesPerSecond,
    double? LeaderboardCacheMissRebuildsPerSecond,
    double? LeaderboardProjectionP95Seconds,
    double? RunnerOnlineCount,
    double? RunnerMinimumAvailableRatio,
    double? PostgreSqlConnectionUsageRatio,
    double? RedisP99Seconds,
    double? DiskAvailableRatio);

public sealed record PlatformMonitoringMetricView(
    PlatformMonitoringMetricKind Kind,
    PlatformMonitoringUnit Unit,
    double? Value,
    PlatformMonitoringStatus Status);

public sealed record PlatformMonitoringView(
    PlatformMonitoringStatus Status,
    bool SourceAvailable,
    DateTimeOffset CapturedAt,
    Uri? DashboardUri,
    IReadOnlyList<PlatformMonitoringMetricView> Metrics);

public interface IPlatformMonitoringReader
{
    Task<PlatformMonitoringMeasurements> ReadAsync(
        CancellationToken cancellationToken);
}

public sealed class ObservePlatformMonitoring(IPlatformMonitoringReader reader)
{
    public async Task<PlatformMonitoringView> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var measurements = await reader.ReadAsync(cancellationToken);
        var metrics = new[]
        {
            Metric(PlatformMonitoringMetricKind.ApiRequestsPerSecond,
                PlatformMonitoringUnit.PerSecond, measurements.ApiRequestsPerSecond),
            Metric(PlatformMonitoringMetricKind.ApiP95Milliseconds,
                PlatformMonitoringUnit.Milliseconds, Milliseconds(measurements.ApiP95Seconds),
                warningAbove: 800),
            Metric(PlatformMonitoringMetricKind.ApiServerErrorPercent,
                PlatformMonitoringUnit.Percent, Percent(measurements.ApiServerErrorRatio),
                criticalAbove: 2),
            Metric(PlatformMonitoringMetricKind.SignalRConnections,
                PlatformMonitoringUnit.Count, measurements.SignalRConnections),
            Metric(PlatformMonitoringMetricKind.CriticalQueueDepth,
                PlatformMonitoringUnit.Count, measurements.CriticalQueueDepth),
            Metric(PlatformMonitoringMetricKind.CriticalQueueOldestSeconds,
                PlatformMonitoringUnit.Seconds, measurements.CriticalQueueOldestSeconds,
                criticalAbove: 15),
            Metric(PlatformMonitoringMetricKind.RuntimeWaitingCount,
                PlatformMonitoringUnit.Count, measurements.RuntimeWaitingCount),
            Metric(PlatformMonitoringMetricKind.RuntimeOldestWaitingSeconds,
                PlatformMonitoringUnit.Seconds, measurements.RuntimeOldestWaitingSeconds,
                criticalAbove: 30),
            Metric(PlatformMonitoringMetricKind.LeaderboardInvalidationsPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardInvalidationsPerSecond),
            Metric(PlatformMonitoringMetricKind.LeaderboardMergeDispatchesPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardMergeDispatchesPerSecond),
            Metric(PlatformMonitoringMetricKind.LeaderboardCacheMissRebuildsPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardCacheMissRebuildsPerSecond),
            Metric(PlatformMonitoringMetricKind.LeaderboardProjectionP95Milliseconds,
                PlatformMonitoringUnit.Milliseconds,
                Milliseconds(measurements.LeaderboardProjectionP95Seconds)),
            Metric(PlatformMonitoringMetricKind.RunnerOnlineCount,
                PlatformMonitoringUnit.Count, measurements.RunnerOnlineCount,
                criticalBelow: 1),
            Metric(PlatformMonitoringMetricKind.RunnerMinimumAvailablePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.RunnerMinimumAvailableRatio), criticalBelow: 15),
            Metric(PlatformMonitoringMetricKind.PostgreSqlConnectionUsagePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.PostgreSqlConnectionUsageRatio), warningAbove: 80),
            Metric(PlatformMonitoringMetricKind.RedisP99Milliseconds,
                PlatformMonitoringUnit.Milliseconds,
                Milliseconds(measurements.RedisP99Seconds), warningAbove: 50),
            Metric(PlatformMonitoringMetricKind.DiskAvailablePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.DiskAvailableRatio), criticalBelow: 15)
        };

        var status = ResolveOverallStatus(measurements.SourceAvailable, metrics);
        return new(
            status,
            measurements.SourceAvailable,
            measurements.CapturedAt,
            measurements.DashboardUri,
            metrics);
    }

    private static PlatformMonitoringMetricView Metric(
        PlatformMonitoringMetricKind kind,
        PlatformMonitoringUnit unit,
        double? value,
        double? warningAbove = null,
        double? criticalAbove = null,
        double? criticalBelow = null)
    {
        value = Finite(value);
        var status = value switch
        {
            null => PlatformMonitoringStatus.Unavailable,
            _ when criticalAbove is not null && value > criticalAbove =>
                PlatformMonitoringStatus.Critical,
            _ when criticalBelow is not null && value < criticalBelow =>
                PlatformMonitoringStatus.Critical,
            _ when warningAbove is not null && value > warningAbove =>
                PlatformMonitoringStatus.Warning,
            _ => PlatformMonitoringStatus.Healthy
        };
        return new(kind, unit, value, status);
    }

    private static PlatformMonitoringStatus ResolveOverallStatus(
        bool sourceAvailable,
        IReadOnlyList<PlatformMonitoringMetricView> metrics)
    {
        if (!sourceAvailable)
            return PlatformMonitoringStatus.Unavailable;
        if (metrics.Any(metric => metric.Status == PlatformMonitoringStatus.Critical))
            return PlatformMonitoringStatus.Critical;
        if (metrics.Any(metric => metric.Status is PlatformMonitoringStatus.Warning
                or PlatformMonitoringStatus.Unavailable))
            return PlatformMonitoringStatus.Warning;
        return PlatformMonitoringStatus.Healthy;
    }

    private static double? Milliseconds(double? seconds) => seconds * 1000;

    private static double? Percent(double? ratio) => ratio * 100;

    private static double? Finite(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;
}
