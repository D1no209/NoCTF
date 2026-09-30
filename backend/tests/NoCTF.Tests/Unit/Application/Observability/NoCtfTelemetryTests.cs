using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application.Observability;

public sealed class NoCtfTelemetryTests
{
    [Test]
    public async Task Webhook_materialization_race_is_counted_without_dynamic_labels()
    {
        long count = 0;
        int tagCount = -1;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && instrument.Name == "noctf.webhook.materialization_races")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            count += value;
            tagCount = tags.Length;
        });
        listener.Start();

        NoCtfTelemetry.RecordWebhookMaterializationRace();

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(tagCount).IsEqualTo(0);
    }

    [Test]
    public async Task Runtime_and_capacity_diagnostics_use_bounded_enum_labels()
    {
        var values = new ConcurrentBag<(string Instrument, string Operation, string? Failure)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && (instrument.Name.StartsWith(
                        "noctf.runner.capacity.transaction_", StringComparison.Ordinal)
                    || instrument.Name == "noctf.runtime.mutation.failures"))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var array = tags.ToArray();
            values.Add((
                instrument.Name,
                array.Single(tag => tag.Key == "operation").Value?.ToString() ?? string.Empty,
                array.SingleOrDefault(tag => tag.Key == "failure").Value?.ToString()));
        });
        listener.Start();

        NoCtfTelemetry.RecordRunnerCapacityTransactionRetry(
            RunnerCapacityTransactionOperation.Claim);
        NoCtfTelemetry.RecordRunnerCapacityTransactionExhaustion(
            RunnerCapacityTransactionOperation.Release);
        NoCtfTelemetry.RecordRuntimeMutationFailure(
            RuntimeOperationMetricKind.PlayerCreate,
            RuntimeMutationFailureCode.RuntimeCapacityExceeded);

        await Assert.That(values).IsEquivalentTo(
        [
            ("noctf.runner.capacity.transaction_retries", "claim", null),
            ("noctf.runner.capacity.transaction_exhaustions", "release", null),
            ("noctf.runtime.mutation.failures", "player_create", "runtime_capacity_exceeded")
        ]);
    }

    [Test]
    public async Task Gameplay_submission_counter_uses_bounded_kinds_and_batch_counts()
    {
        var measurements = new ConcurrentBag<(string Kind, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && instrument.Name == "noctf.gameplay_fact.submissions")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var kind = tags.ToArray()
                .Single(tag => tag.Key == "kind")
                .Value?.ToString();
            measurements.Add((kind ?? string.Empty, value));
        });
        listener.Start();

        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.FlagAttempt, 4);
        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.BreakAttempt, 2);
        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.FixAttempt);
        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.HintUnlock, 5);

        await Assert.That(measurements).IsEquivalentTo(
        [
            ("flag", 4L),
            ("break", 2L),
            ("fix", 1L)
        ]);
    }

    [Test]
    public async Task Gameplay_processing_records_bounded_terminal_outcomes_and_duration()
    {
        var outcomes = new ConcurrentBag<string>();
        var durations = new ConcurrentBag<(string Outcome, double Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && instrument.Name.StartsWith(
                    "noctf.gameplay_fact.processing",
                    StringComparison.Ordinal))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "noctf.gameplay_fact.processing")
                return;
            outcomes.Add(tags.ToArray().Single(tag => tag.Key == "outcome")
                .Value?.ToString() ?? string.Empty);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name != "noctf.gameplay_fact.processing.duration")
                return;
            durations.Add((
                tags.ToArray().Single(tag => tag.Key == "outcome")
                    .Value?.ToString() ?? string.Empty,
                value));
        });
        listener.Start();

        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.FlagAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Correct,
            0.25);
        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.BreakAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Wrong,
            0.5);
        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.FlagAttempt,
            GameplayFactState.PlatformFailed,
            null,
            1.25);
        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.FixAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Correct,
            2);

        await Assert.That(outcomes).IsEquivalentTo(
            ["correct", "incorrect", "platform_error"]);
        await Assert.That(durations).IsEquivalentTo(
        [
            ("correct", 0.25),
            ("incorrect", 0.5),
            ("platform_error", 1.25)
        ]);
    }

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
            availableCpuMillicores: 2,
            totalCpuMillicores: 4,
            availablePids: 10,
            totalPids: 20);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
            pool,
            "runner-2",
            online: true,
            availableMemoryBytes: 300,
            totalMemoryBytes: 600,
            availableCpuMillicores: 6,
            totalCpuMillicores: 8,
            availablePids: 30,
            totalPids: 40);
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
            pool,
            "runner-offline",
            online: false,
            availableMemoryBytes: 999,
            totalMemoryBytes: 999,
            availableCpuMillicores: 999,
            totalCpuMillicores: 999,
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
