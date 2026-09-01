using System.Text.Json;
using NoCTF.Application.Administration.Monitoring;

namespace NoCTF.Tests.Unit.Application;

public sealed class PlatformMonitoringTests
{
    [Test]
    public async Task HealthyMeasurements_AreConvertedForTheProtocolView()
    {
        var useCase = UseCase(Measurements());

        var result = await useCase.ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Healthy);
        await Assert.That(result.PrometheusAvailable).IsTrue();
        await Assert.That(result.NatsAvailable).IsTrue();
        await Assert.That(Metric(result, PlatformMonitoringMetricKind.ApiP95Milliseconds).Value)
            .IsEqualTo(250);
        await Assert.That(Metric(result, PlatformMonitoringMetricKind.ApiServerErrorPercent).Value)
            .IsEqualTo(0.5);
        await Assert.That(Metric(result, PlatformMonitoringMetricKind.DiskAvailablePercent).Value)
            .IsEqualTo(60);
    }

    [Test]
    public async Task CriticalThreshold_TakesPrecedenceOverWarnings()
    {
        var useCase = UseCase(Measurements(
            apiP95Seconds: 1.2,
            runtimeOldestWaitingSeconds: 31));

        var result = await useCase.ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Critical);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.ApiP95Milliseconds).Status)
            .IsEqualTo(PlatformMonitoringStatus.Warning);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.RuntimeOldestWaitingSeconds).Status)
            .IsEqualTo(PlatformMonitoringStatus.Critical);
    }

    [Test]
    public async Task TransientQueueMessage_DoesNotTriggerSustainedThreshold()
    {
        var measurements = Measurements() with
        {
            CriticalQueuePendingCount = PlatformMonitoringSample.From(
                value: 80,
                thresholdValue: 0)
        };

        var result = await UseCase(measurements).ExecuteAsync();

        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.CriticalQueuePendingCount).Value)
            .IsEqualTo(80);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.CriticalQueuePendingCount).Status)
            .IsEqualTo(PlatformMonitoringStatus.Healthy);
    }

    [Test]
    public async Task NatsUnavailable_IsCriticalWhilePrometheusIsAvailable()
    {
        var measurements = Measurements() with
        {
            NatsAvailability = PlatformMonitoringSample.From(0)
        };

        var result = await UseCase(measurements).ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Critical);
        await Assert.That(result.NatsAvailable).IsFalse();
        await Assert.That(Metric(result, PlatformMonitoringMetricKind.NatsAvailability).Status)
            .IsEqualTo(PlatformMonitoringStatus.Critical);
    }

    [Test]
    public async Task UnavailablePrometheus_DoesNotPretendThePlatformIsHealthy()
    {
        var measurements = Measurements() with { PrometheusAvailable = false };

        var result = await UseCase(measurements).ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Unavailable);
        await Assert.That(result.PrometheusAvailable).IsFalse();
    }

    [Test]
    public async Task IdleLatency_IsNoSamplesWithoutDegradingOverallStatus()
    {
        var measurements = Measurements() with
        {
            ApiP95Seconds = PlatformMonitoringSample.NoSamples(),
            LeaderboardProjectionP95Seconds = PlatformMonitoringSample.NoSamples(),
            RedisP99Seconds = PlatformMonitoringSample.NoSamples()
        };

        var result = await UseCase(measurements).ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Healthy);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.ApiP95Milliseconds).Status)
            .IsEqualTo(PlatformMonitoringStatus.NoSamples);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.LeaderboardProjectionP95Milliseconds).Status)
            .IsEqualTo(PlatformMonitoringStatus.NoSamples);
    }

    [Test]
    public async Task NonFiniteMeasurements_AreUnavailableAndJsonSafe()
    {
        var measurements = Measurements() with
        {
            ApiRequestsPerSecond = PlatformMonitoringSample.From(double.PositiveInfinity),
            ApiP95Seconds = PlatformMonitoringSample.From(double.NaN),
            DiskAvailableRatio = PlatformMonitoringSample.From(double.NegativeInfinity)
        };

        var result = await UseCase(measurements).ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Warning);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.ApiRequestsPerSecond).Value)
            .IsNull();
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.ApiP95Milliseconds).Status)
            .IsEqualTo(PlatformMonitoringStatus.Unavailable);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.DiskAvailablePercent).Value)
            .IsNull();
        await Assert.That(result.Metrics
                .Where(metric => metric.Value is not null)
                .All(metric => double.IsFinite(metric.Value!.Value)))
            .IsTrue();
        await Assert.That(() => JsonSerializer.Serialize(result)).ThrowsNothing();
    }

    private static ObservePlatformMonitoring UseCase(
        PlatformMonitoringMeasurements measurements) =>
        new(new StubReader(measurements), PlatformMonitoringThresholds.Default);

    private static PlatformMonitoringMetricView Metric(
        PlatformMonitoringView view,
        PlatformMonitoringMetricKind kind) =>
        view.Metrics.Single(metric => metric.Kind == kind);

    private static PlatformMonitoringMeasurements Measurements(
        double apiP95Seconds = 0.25,
        double runtimeOldestWaitingSeconds = 1)
    {
        static PlatformMonitoringSample Value(double value) =>
            PlatformMonitoringSample.From(value);

        return new(
            true,
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            new Uri("https://monitoring.example.test/"),
            Value(12),
            Value(apiP95Seconds),
            Value(0.005),
            Value(8),
            Value(1),
            Value(0.10),
            Value(0),
            Value(0),
            Value(0),
            Value(0),
            Value(0),
            Value(0),
            Value(runtimeOldestWaitingSeconds),
            Value(0),
            Value(0),
            Value(0.1),
            Value(0),
            Value(0),
            Value(2),
            Value(0.75),
            Value(0.45),
            Value(0.01),
            Value(0.60));
    }

    private sealed class StubReader(PlatformMonitoringMeasurements measurements)
        : IPlatformMonitoringReader
    {
        public Task<PlatformMonitoringMeasurements> ReadAsync(
            CancellationToken cancellationToken) => Task.FromResult(measurements);
    }
}
