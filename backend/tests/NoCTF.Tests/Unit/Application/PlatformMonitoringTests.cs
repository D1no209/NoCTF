using System.Text.Json;
using NoCTF.Application.Administration.Monitoring;

namespace NoCTF.Tests.Unit.Application;

public sealed class PlatformMonitoringTests
{
    [Test]
    [Arguments(0, 1.2, 1.2, PlatformMonitoringStatus.NoSamples)]
    [Arguments(2, 1.2, 1.2, PlatformMonitoringStatus.InsufficientSamples)]
    [Arguments(100, 0.012, 0.012, PlatformMonitoringStatus.Healthy)]
    [Arguments(100, 1.2, 0.2, PlatformMonitoringStatus.Observing)]
    [Arguments(100, 1.2, 1.2, PlatformMonitoringStatus.Warning)]
    public async Task Latency_requires_samples_and_sustained_evidence_and_converts_seconds_once(
        int samples, double current, double sustained, PlatformMonitoringStatus expected)
    {
        var sample = samples == 0 ? PlatformMonitoringSample.NoSamples() : PlatformMonitoringSample.From(current, sustained);
        sample = sample with { SampleCount = samples, MinimumSamples = 100, WindowSeconds = 300 };
        var result = await UseCase(Measurements() with { ApiP95Seconds = sample, RedisP99Seconds = sample }).ExecuteAsync();
        var api = Metric(result, PlatformMonitoringMetricKind.ApiP95Milliseconds);
        await Assert.That(api.Status).IsEqualTo(expected);
        await Assert.That(api.Value).IsEqualTo(samples == 0 ? null : current * 1000);
        await Assert.That(api.SampleCount).IsEqualTo(samples);
        await Assert.That(api.WindowSeconds).IsEqualTo(300);
    }

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
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FlagSubmissionsPerSecond).Value)
            .IsEqualTo(8);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FlagSubmissionsLastFiveMinutes).Value)
            .IsEqualTo(2400);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FlagCorrectPercent).Value)
            .IsEqualTo(72);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FlagProcessingP95Milliseconds).Value)
            .IsEqualTo(420);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FlagPlatformErrorPercent).Value)
            .IsEqualTo(0.1);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FixSubmissionsPerSecond).Value)
            .IsEqualTo(0.2);
        await Assert.That(Metric(result,
                PlatformMonitoringMetricKind.FixSubmissionsLastFiveMinutes).Value)
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
    public async Task Incorrect_flags_do_not_degrade_platform_health_but_platform_failures_do()
    {
        var lowCorrectRate = Measurements() with
        {
            FlagCorrectRatio = PlatformMonitoringSample.From(0.01) with
            {
                SampleCount = 200,
                MinimumSamples = 20,
                WindowSeconds = 300
            }
        };
        var platformFailures = lowCorrectRate with
        {
            FlagPlatformErrorRatio = PlatformMonitoringSample.From(0.03) with
            {
                SampleCount = 200,
                MinimumSamples = 20,
                WindowSeconds = 300
            }
        };

        var playerResult = await UseCase(lowCorrectRate).ExecuteAsync();
        var platformResult = await UseCase(platformFailures).ExecuteAsync();

        await Assert.That(playerResult.Status).IsEqualTo(PlatformMonitoringStatus.Healthy);
        await Assert.That(Metric(playerResult,
                PlatformMonitoringMetricKind.FlagCorrectPercent).Value)
            .IsEqualTo(1);
        await Assert.That(platformResult.Status).IsEqualTo(PlatformMonitoringStatus.Critical);
        await Assert.That(Metric(platformResult,
                PlatformMonitoringMetricKind.FlagPlatformErrorPercent).Status)
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
            Value(0.60),
            Value(8) with { WindowSeconds = 300 },
            Value(2400) with { WindowSeconds = 300 },
            Value(0.2) with { WindowSeconds = 300 },
            Value(60) with { WindowSeconds = 300 },
            Value(0.72) with
            {
                SampleCount = 200,
                MinimumSamples = 20,
                WindowSeconds = 300
            },
            Value(0.42) with
            {
                SampleCount = 200,
                MinimumSamples = 20,
                WindowSeconds = 300
            },
            Value(0.001) with
            {
                SampleCount = 200,
                MinimumSamples = 20,
                WindowSeconds = 300
            });
    }

    private sealed class StubReader(PlatformMonitoringMeasurements measurements)
        : IPlatformMonitoringReader
    {
        public Task<PlatformMonitoringMeasurements> ReadAsync(
            CancellationToken cancellationToken) => Task.FromResult(measurements);
    }
}
