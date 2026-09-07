namespace NoCTF.Application.Administration.Monitoring;

public enum PlatformMonitoringStatus : short
{
    Healthy,
    Warning,
    Critical,
    Unavailable,
    NoSamples,
    InsufficientSamples,
    Observing
}

public enum PlatformMonitoringMetricKind : short
{
    ApiRequestsPerSecond,
    ApiP95Milliseconds,
    ApiServerErrorPercent,
    SignalRConnections,
    NatsAvailability,
    JetStreamStorageUsagePercent,
    CriticalQueuePendingCount,
    CriticalQueueAckPendingCount,
    CriticalQueueRedeliveredCount,
    WolverineOutboxCount,
    WolverineInboxCount,
    RuntimeWaitingCount,
    RuntimeOldestWaitingSeconds,
    LeaderboardMergeDispatchFailuresPerSecond,
    LeaderboardCacheMissRebuildFailuresPerSecond,
    LeaderboardProjectionP95Milliseconds,
    LeaderboardPublishFailuresPerSecond,
    LeaderboardSignalRPublishFailuresPerSecond,
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

public enum PlatformMonitoringSampleState : short
{
    Available,
    NoSamples,
    Unavailable
}

public readonly record struct PlatformMonitoringSample(
    double? Value,
    PlatformMonitoringSampleState State,
    double? ThresholdValue = null,
    double? SampleCount = null,
    int? MinimumSamples = null,
    int? WindowSeconds = null)
{
    public static PlatformMonitoringSample From(
        double value,
        double? thresholdValue = null) =>
        new(value, PlatformMonitoringSampleState.Available, thresholdValue);

    public static PlatformMonitoringSample NoSamples() =>
        new(null, PlatformMonitoringSampleState.NoSamples);

    public static PlatformMonitoringSample Unavailable() =>
        new(null, PlatformMonitoringSampleState.Unavailable);
}

public sealed record PlatformMonitoringThresholds(
    int SustainedWindowMinutes,
    int PendingWarning,
    int PendingCritical,
    int AckPendingWarning,
    int AckPendingCritical,
    int RedeliveryWarning,
    int RedeliveryCritical,
    int OutboxWarning,
    int OutboxCritical,
    int InboxWarning,
    int InboxCritical,
    double JetStreamStorageWarningPercent,
    double JetStreamStorageCriticalPercent,
    int LatencyMinimumSamples = 100,
    int LatencySustainedWindowMinutes = 3)
{
    public static PlatformMonitoringThresholds Default { get; } = new(
        SustainedWindowMinutes: 3,
        PendingWarning: 25,
        PendingCritical: 100,
        AckPendingWarning: 10,
        AckPendingCritical: 50,
        RedeliveryWarning: 1,
        RedeliveryCritical: 10,
        OutboxWarning: 25,
        OutboxCritical: 100,
        InboxWarning: 25,
        InboxCritical: 100,
        JetStreamStorageWarningPercent: 75,
        JetStreamStorageCriticalPercent: 90);
}

public sealed record PlatformMonitoringMeasurements(
    bool PrometheusAvailable,
    DateTimeOffset CapturedAt,
    Uri? DashboardUri,
    PlatformMonitoringSample ApiRequestsPerSecond,
    PlatformMonitoringSample ApiP95Seconds,
    PlatformMonitoringSample ApiServerErrorRatio,
    PlatformMonitoringSample SignalRConnections,
    PlatformMonitoringSample NatsAvailability,
    PlatformMonitoringSample JetStreamStorageUsageRatio,
    PlatformMonitoringSample CriticalQueuePendingCount,
    PlatformMonitoringSample CriticalQueueAckPendingCount,
    PlatformMonitoringSample CriticalQueueRedeliveredCount,
    PlatformMonitoringSample WolverineOutboxCount,
    PlatformMonitoringSample WolverineInboxCount,
    PlatformMonitoringSample RuntimeWaitingCount,
    PlatformMonitoringSample RuntimeOldestWaitingSeconds,
    PlatformMonitoringSample LeaderboardMergeDispatchFailuresPerSecond,
    PlatformMonitoringSample LeaderboardCacheMissRebuildFailuresPerSecond,
    PlatformMonitoringSample LeaderboardProjectionP95Seconds,
    PlatformMonitoringSample LeaderboardPublishFailuresPerSecond,
    PlatformMonitoringSample LeaderboardSignalRPublishFailuresPerSecond,
    PlatformMonitoringSample RunnerOnlineCount,
    PlatformMonitoringSample RunnerMinimumAvailableRatio,
    PlatformMonitoringSample PostgreSqlConnectionUsageRatio,
    PlatformMonitoringSample RedisP99Seconds,
    PlatformMonitoringSample DiskAvailableRatio,
    IReadOnlyList<PlatformMonitoringLatencyMeasurement>? LatencyDetails = null,
    IReadOnlyList<PlatformMonitoringPoolResource>? PoolResources = null);

public enum PlatformMonitoringLatencyKind { Rest, SignalR, Upload, Download, RedisOperation }
public enum PlatformMonitoringResource { Memory, Cpu, Pids }

public sealed record PlatformMonitoringLatencyMeasurement(
    PlatformMonitoringLatencyKind Kind, string Endpoint,
    PlatformMonitoringSample P95Seconds, PlatformMonitoringSample P99Seconds,
    PlatformMonitoringSample MeanSeconds, PlatformMonitoringSample Rate, PlatformMonitoringSample ErrorRatio);

public sealed record PlatformMonitoringLatencyView(
    PlatformMonitoringLatencyKind Kind, string Endpoint, double? P95Milliseconds, double? P99Milliseconds,
    double? MeanMilliseconds, double? RequestsPerSecond, double? ErrorPercent,
    double? SampleCount, int MinimumSamples, int WindowSeconds, PlatformMonitoringStatus Status);

public sealed record PlatformMonitoringPoolResource(
    string Pool, PlatformMonitoringResource Resource, double? Available, double? Total, double? OnlineRunners);

public sealed record PlatformMonitoringMetricView(
    PlatformMonitoringMetricKind Kind,
    PlatformMonitoringUnit Unit,
    double? Value,
    PlatformMonitoringStatus Status,
    double? SampleCount = null,
    int? MinimumSamples = null,
    int? WindowSeconds = null);

public sealed record PlatformMonitoringView(
    PlatformMonitoringStatus Status,
    bool PrometheusAvailable,
    bool NatsAvailable,
    DateTimeOffset CapturedAt,
    Uri? DashboardUri,
    IReadOnlyList<PlatformMonitoringMetricView> Metrics,
    IReadOnlyList<PlatformMonitoringLatencyView>? LatencyDetails = null,
    IReadOnlyList<PlatformMonitoringPoolResource>? PoolResources = null,
    int LatencySustainedWindowMinutes = 3);

public interface IPlatformMonitoringReader
{
    Task<PlatformMonitoringMeasurements> ReadAsync(
        CancellationToken cancellationToken);
}

public sealed class ObservePlatformMonitoring(
    IPlatformMonitoringReader reader,
    PlatformMonitoringThresholds thresholds)
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
                warningAbove: 800, noSamplesStatus: PlatformMonitoringStatus.NoSamples),
            Metric(PlatformMonitoringMetricKind.ApiServerErrorPercent,
                PlatformMonitoringUnit.Percent, Percent(measurements.ApiServerErrorRatio),
                criticalAbove: 2),
            Metric(PlatformMonitoringMetricKind.SignalRConnections,
                PlatformMonitoringUnit.Count, measurements.SignalRConnections),
            Metric(PlatformMonitoringMetricKind.NatsAvailability,
                PlatformMonitoringUnit.Count, measurements.NatsAvailability,
                criticalBelow: 1,
                noSamplesStatus: PlatformMonitoringStatus.Critical,
                unavailableStatus: PlatformMonitoringStatus.Critical),
            Metric(PlatformMonitoringMetricKind.JetStreamStorageUsagePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.JetStreamStorageUsageRatio),
                warningAbove: thresholds.JetStreamStorageWarningPercent,
                criticalAbove: thresholds.JetStreamStorageCriticalPercent),
            Metric(PlatformMonitoringMetricKind.CriticalQueuePendingCount,
                PlatformMonitoringUnit.Count, measurements.CriticalQueuePendingCount,
                warningAbove: thresholds.PendingWarning,
                criticalAbove: thresholds.PendingCritical),
            Metric(PlatformMonitoringMetricKind.CriticalQueueAckPendingCount,
                PlatformMonitoringUnit.Count, measurements.CriticalQueueAckPendingCount,
                warningAbove: thresholds.AckPendingWarning,
                criticalAbove: thresholds.AckPendingCritical),
            Metric(PlatformMonitoringMetricKind.CriticalQueueRedeliveredCount,
                PlatformMonitoringUnit.Count, measurements.CriticalQueueRedeliveredCount,
                warningAbove: thresholds.RedeliveryWarning,
                criticalAbove: thresholds.RedeliveryCritical),
            Metric(PlatformMonitoringMetricKind.WolverineOutboxCount,
                PlatformMonitoringUnit.Count, measurements.WolverineOutboxCount,
                warningAbove: thresholds.OutboxWarning,
                criticalAbove: thresholds.OutboxCritical),
            Metric(PlatformMonitoringMetricKind.WolverineInboxCount,
                PlatformMonitoringUnit.Count, measurements.WolverineInboxCount,
                warningAbove: thresholds.InboxWarning,
                criticalAbove: thresholds.InboxCritical),
            Metric(PlatformMonitoringMetricKind.RuntimeWaitingCount,
                PlatformMonitoringUnit.Count, measurements.RuntimeWaitingCount),
            Metric(PlatformMonitoringMetricKind.RuntimeOldestWaitingSeconds,
                PlatformMonitoringUnit.Seconds, measurements.RuntimeOldestWaitingSeconds,
                criticalAbove: 30),
            Metric(PlatformMonitoringMetricKind.LeaderboardMergeDispatchFailuresPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardMergeDispatchFailuresPerSecond,
                warningAbove: 0),
            Metric(PlatformMonitoringMetricKind.LeaderboardCacheMissRebuildFailuresPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardCacheMissRebuildFailuresPerSecond,
                warningAbove: 0),
            Metric(PlatformMonitoringMetricKind.LeaderboardProjectionP95Milliseconds,
                PlatformMonitoringUnit.Milliseconds,
                Milliseconds(measurements.LeaderboardProjectionP95Seconds),
                noSamplesStatus: PlatformMonitoringStatus.NoSamples),
            Metric(PlatformMonitoringMetricKind.LeaderboardPublishFailuresPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardPublishFailuresPerSecond,
                warningAbove: 0),
            Metric(PlatformMonitoringMetricKind.LeaderboardSignalRPublishFailuresPerSecond,
                PlatformMonitoringUnit.PerSecond,
                measurements.LeaderboardSignalRPublishFailuresPerSecond,
                warningAbove: 0),
            Metric(PlatformMonitoringMetricKind.RunnerOnlineCount,
                PlatformMonitoringUnit.Count, measurements.RunnerOnlineCount,
                criticalBelow: 1),
            Metric(PlatformMonitoringMetricKind.RunnerMinimumAvailablePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.RunnerMinimumAvailableRatio), criticalBelow: 15,
                noSamplesStatus: PlatformMonitoringStatus.NoSamples),
            Metric(PlatformMonitoringMetricKind.PostgreSqlConnectionUsagePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.PostgreSqlConnectionUsageRatio), warningAbove: 80),
            Metric(PlatformMonitoringMetricKind.RedisP99Milliseconds,
                PlatformMonitoringUnit.Milliseconds,
                Milliseconds(measurements.RedisP99Seconds), warningAbove: 50,
                noSamplesStatus: PlatformMonitoringStatus.NoSamples),
            Metric(PlatformMonitoringMetricKind.DiskAvailablePercent,
                PlatformMonitoringUnit.Percent,
                Percent(measurements.DiskAvailableRatio), criticalBelow: 15)
        };

        var status = ResolveOverallStatus(measurements.PrometheusAvailable, metrics);
        var natsAvailable = Metric(metrics, PlatformMonitoringMetricKind.NatsAvailability)
            .Status == PlatformMonitoringStatus.Healthy;
        return new(
            status,
            measurements.PrometheusAvailable,
            natsAvailable,
            measurements.CapturedAt,
            measurements.DashboardUri,
            metrics,
            (measurements.LatencyDetails ?? []).Select(LatencyView).ToArray(),
            measurements.PoolResources ?? [],
            thresholds.LatencySustainedWindowMinutes);
    }

    private PlatformMonitoringLatencyView LatencyView(PlatformMonitoringLatencyMeasurement row)
    {
        var sample = row.Kind == PlatformMonitoringLatencyKind.RedisOperation ? row.P99Seconds : row.P95Seconds;
        var metric = Metric(PlatformMonitoringMetricKind.ApiP95Milliseconds, PlatformMonitoringUnit.Milliseconds,
            Milliseconds(sample), warningAbove: row.Kind switch
            {
                PlatformMonitoringLatencyKind.Rest => 800,
                PlatformMonitoringLatencyKind.RedisOperation => 50,
                _ => null
            }, noSamplesStatus: PlatformMonitoringStatus.NoSamples);
        return new(row.Kind, row.Endpoint, Finite(row.P95Seconds.Value * 1000), Finite(row.P99Seconds.Value * 1000),
            sample.SampleCount > 0 ? Finite(row.MeanSeconds.Value * 1000) : null, Finite(row.Rate.Value), Finite(row.ErrorRatio.Value * 100),
            sample.SampleCount, thresholds.LatencyMinimumSamples, 300, metric.Status);
    }

    private static PlatformMonitoringMetricView Metric(
        PlatformMonitoringMetricKind kind,
        PlatformMonitoringUnit unit,
        PlatformMonitoringSample sample,
        double? warningAbove = null,
        double? criticalAbove = null,
        double? criticalBelow = null,
        PlatformMonitoringStatus noSamplesStatus = PlatformMonitoringStatus.Unavailable,
        PlatformMonitoringStatus unavailableStatus = PlatformMonitoringStatus.Unavailable)
    {
        var value = Finite(sample.Value);
        var thresholdValue = Finite(sample.ThresholdValue) ?? value;
        var status = sample.State switch
        {
            PlatformMonitoringSampleState.Unavailable => unavailableStatus,
            PlatformMonitoringSampleState.NoSamples => noSamplesStatus,
            _ when value is null => unavailableStatus,
            _ when sample.MinimumSamples.HasValue && sample.SampleCount < sample.MinimumSamples =>
                PlatformMonitoringStatus.InsufficientSamples,
            _ when criticalAbove is not null && thresholdValue > criticalAbove =>
                PlatformMonitoringStatus.Critical,
            _ when criticalBelow is not null && thresholdValue < criticalBelow =>
                PlatformMonitoringStatus.Critical,
            _ when warningAbove is not null && thresholdValue > warningAbove =>
                PlatformMonitoringStatus.Warning,
            _ when sample.MinimumSamples.HasValue && warningAbove is not null && value > warningAbove =>
                PlatformMonitoringStatus.Observing,
            _ => PlatformMonitoringStatus.Healthy
        };
        return new(kind, unit, value, status, Finite(sample.SampleCount), sample.MinimumSamples, sample.WindowSeconds);
    }

    private static PlatformMonitoringMetricView Metric(
        IReadOnlyList<PlatformMonitoringMetricView> metrics,
        PlatformMonitoringMetricKind kind) =>
        metrics.Single(metric => metric.Kind == kind);

    private static PlatformMonitoringStatus ResolveOverallStatus(
        bool prometheusAvailable,
        IReadOnlyList<PlatformMonitoringMetricView> metrics)
    {
        if (!prometheusAvailable)
            return PlatformMonitoringStatus.Unavailable;
        if (metrics.Any(metric => metric.Status == PlatformMonitoringStatus.Critical))
            return PlatformMonitoringStatus.Critical;
        if (metrics.Any(metric => metric.Status is PlatformMonitoringStatus.Warning
                or PlatformMonitoringStatus.Unavailable))
        {
            return PlatformMonitoringStatus.Warning;
        }
        return PlatformMonitoringStatus.Healthy;
    }

    private static PlatformMonitoringSample Milliseconds(PlatformMonitoringSample sample) =>
        sample with
        {
            Value = sample.Value * 1000,
            ThresholdValue = sample.ThresholdValue * 1000
        };

    private static PlatformMonitoringSample Percent(PlatformMonitoringSample sample) =>
        sample with
        {
            Value = sample.Value * 100,
            ThresholdValue = sample.ThresholdValue * 100
        };

    private static double? Finite(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;
}
