using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using NoCTF.Application.Observability;

namespace NoCTF.Tests.Unit.Application.Observability;

public sealed class NoCtfTelemetryTests
{
    [Test]
    public async Task Pools_remain_separate_and_offline_nodes_change_quota_and_online_count_independently()
    {
        var pool = $"pool-{Guid.NewGuid():N}";
        var other = pool + "-other";
        var zero = pool + "-zero";
        var values = new ConcurrentDictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName) meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string group = "", resource = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "pool") group = tag.Value?.ToString() ?? "";
                if (tag.Key == "resource") resource = tag.Value?.ToString() ?? "";
            }
            if (group.StartsWith(pool, StringComparison.Ordinal)) values[$"{group}:{instrument.Name}:{resource}"] = value;
        });
        listener.Start();
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(pool, "full", true, 0, 100, 10, 10, 10, 10);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(pool, "free", true, 300, 300, 30, 30, 30, 30);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(other, "single", true, 20, 100, 10, 10, 10, 10);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(zero, "empty-quota", true, 0, 0, 0, 0, 0, 0);
        listener.RecordObservableInstruments();
        double Ratio(string group) => values[$"{group}:noctf.runner.capacity.available:memory"]
            / (double)values[$"{group}:noctf.runner.capacity.total:memory"];
        await Assert.That(Ratio(pool)).IsEqualTo(0.75); // Node-level minimum would incorrectly be zero.
        await Assert.That(Ratio(other)).IsEqualTo(0.2);
        await Assert.That(values[$"{zero}:noctf.runner.capacity.total:memory"]).IsEqualTo(0);
        await Assert.That(values[$"{pool}:noctf.runner.online:"]).IsEqualTo(2);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(pool, "full", false, 0, 100, 10, 10, 10, 10);
        listener.RecordObservableInstruments();
        await Assert.That(Ratio(pool)).IsEqualTo(1);
        await Assert.That(values[$"{pool}:noctf.runner.online:"]).IsEqualTo(1);
        await Assert.That(Ratio(other)).IsEqualTo(0.2);
    }

    [Test]
    public async Task Runner_capacity_is_aggregated_across_online_runners_in_the_same_pool()
    {
        var pool = $"test-{Guid.NewGuid():N}";
        var measurements = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && instrument.Name.StartsWith("noctf.runner.", StringComparison.Ordinal))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? measurementPool = null;
            string? resource = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "pool")
                    measurementPool = tag.Value?.ToString();
                else if (tag.Key == "resource")
                    resource = tag.Value?.ToString();
            }

            if (measurementPool == pool)
                measurements[$"{instrument.Name}:{resource}"] = value;
        });
        listener.Start();

        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
            pool,
            "runner-1",
            online: true,
            availableMemoryBytes: 100,
            totalMemoryBytes: 200,
            availableNanoCpus: 2,
            totalNanoCpus: 4,
            availablePids: 10,
            totalPids: 20);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
            pool,
            "runner-2",
            online: true,
            availableMemoryBytes: 300,
            totalMemoryBytes: 600,
            availableNanoCpus: 6,
            totalNanoCpus: 8,
            availablePids: 30,
            totalPids: 40);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
            pool,
            "runner-offline",
            online: false,
            availableMemoryBytes: 999,
            totalMemoryBytes: 999,
            availableNanoCpus: 999,
            totalNanoCpus: 999,
            availablePids: 999,
            totalPids: 999);

        listener.RecordObservableInstruments();

        await Assert.That(measurements["noctf.runner.online:"]).IsEqualTo(2);
        await Assert.That(measurements["noctf.runner.capacity.available:memory"]).IsEqualTo(400);
        await Assert.That(measurements["noctf.runner.capacity.total:memory"]).IsEqualTo(800);
        await Assert.That(measurements["noctf.runner.capacity.available:cpu"]).IsEqualTo(8);
        await Assert.That(measurements["noctf.runner.capacity.total:cpu"]).IsEqualTo(12);
        await Assert.That(measurements["noctf.runner.capacity.available:pids"]).IsEqualTo(40);
        await Assert.That(measurements["noctf.runner.capacity.total:pids"]).IsEqualTo(60);
    }
}
