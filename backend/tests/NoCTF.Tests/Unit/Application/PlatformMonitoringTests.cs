using NoCTF.Application.Administration.Monitoring;

namespace NoCTF.Tests.Unit.Application;

public sealed class PlatformMonitoringTests
{
    [Test]
    public async Task HealthyMeasurements_AreConvertedForTheProtocolView()
    {
        var useCase = new ObservePlatformMonitoring(new StubReader(Measurements()));

        var result = await useCase.ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Healthy);
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
        var useCase = new ObservePlatformMonitoring(new StubReader(Measurements(
            apiP95Seconds: 1.2,
            runtimeOldestWaitingSeconds: 31)));

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
    public async Task UnavailableSource_DoesNotPretendThePlatformIsHealthy()
    {
        var measurements = Measurements() with { SourceAvailable = false };
        var useCase = new ObservePlatformMonitoring(new StubReader(measurements));

        var result = await useCase.ExecuteAsync();

        await Assert.That(result.Status).IsEqualTo(PlatformMonitoringStatus.Unavailable);
        await Assert.That(result.SourceAvailable).IsFalse();
    }

    private static PlatformMonitoringMetricView Metric(
        PlatformMonitoringView view,
        PlatformMonitoringMetricKind kind) =>
        view.Metrics.Single(metric => metric.Kind == kind);

    private static PlatformMonitoringMeasurements Measurements(
        double apiP95Seconds = 0.25,
        double runtimeOldestWaitingSeconds = 1) =>
        new(
            true,
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            new Uri("https://monitoring.example.test/"),
            12,
            apiP95Seconds,
            0.005,
            8,
            0,
            0,
            0,
            runtimeOldestWaitingSeconds,
            0,
            0,
            2,
            0.75,
            0.45,
            0.01,
            0.60);

    private sealed class StubReader(PlatformMonitoringMeasurements measurements)
        : IPlatformMonitoringReader
    {
        public Task<PlatformMonitoringMeasurements> ReadAsync(
            CancellationToken cancellationToken) => Task.FromResult(measurements);
    }
}
