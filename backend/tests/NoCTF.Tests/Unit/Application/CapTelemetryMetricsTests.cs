using System.Diagnostics.Metrics;
using NoCTF.Application.Admission;
using NoCTF.Application.Observability;

namespace NoCTF.Tests.Unit.Application;

[NotInParallel]
public sealed class CapTelemetryMetricsTests
{
    [Test]
    public async Task Cap_metrics_have_bounded_labels_and_distinct_time_semantics()
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var histogramTags = new Dictionary<string, string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscribed) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && instrument.Name.StartsWith("noctf.cap.", StringComparison.Ordinal))
                subscribed.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            values[instrument.Name] = measurement);
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            values[instrument.Name] = measurement;
            foreach (var tag in tags)
                histogramTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        });
        listener.Start();

        try
        {
            NoCtfTelemetry.SetCapTelemetryActive(true);
            NoCtfTelemetry.SetCapTelemetrySnapshot(
                new CapDailyTelemetry(12, 3, 2, 1.45),
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
            NoCtfTelemetry.RecordCapSiteverify(
                HumanVerificationAction.Login, HumanVerificationResult.Verified, 0.025);
            listener.RecordObservableInstruments();

            await Assert.That(values["noctf.cap.active"]).IsEqualTo(1);
            await Assert.That(values["noctf.cap.telemetry.available"]).IsEqualTo(1);
            await Assert.That(values["noctf.cap.verified.today"]).IsEqualTo(12);
            await Assert.That(values["noctf.cap.failed.today"]).IsEqualTo(3);
            await Assert.That(values["noctf.cap.rate_limited.today"]).IsEqualTo(2);
            await Assert.That(values["noctf.cap.average_solve.duration"]).IsEqualTo(1.45);
            await Assert.That(values["noctf.cap.siteverify.duration"]).IsEqualTo(0.025);
            await Assert.That(histogramTags["action"]).IsEqualTo("login");
            await Assert.That(histogramTags["outcome"]).IsEqualTo("verified");
            await Assert.That(histogramTags.Keys)
                .IsEquivalentTo(["action", "outcome"]);

            NoCtfTelemetry.SetCapTelemetryUnavailable();
            listener.RecordObservableInstruments();
            await Assert.That(values["noctf.cap.telemetry.available"]).IsEqualTo(0);
            await Assert.That(values["noctf.cap.verified.today"]).IsEqualTo(12);
        }
        finally
        {
            NoCtfTelemetry.SetCapTelemetryActive(false);
            NoCtfTelemetry.ClearCapTelemetrySnapshot();
        }
    }
}
