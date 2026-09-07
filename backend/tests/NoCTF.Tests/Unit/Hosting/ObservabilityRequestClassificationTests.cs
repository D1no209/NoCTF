using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using NoCTF.Application.Observability;
using NoCTF.Hosting.Observability;

namespace NoCTF.Tests.Unit.Hosting;

[NotInParallel]
public sealed class ObservabilityRequestClassificationTests
{
    [Test]
    public async Task Pipeline_classifies_success_and_failures_consistently_and_excludes_static_health_requests()
    {
        var counts = new ConcurrentDictionary<string, string>();
        var durations = new ConcurrentDictionary<string, string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName) owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "noctf.api.requests") Capture(counts, tags);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "noctf.api.request.duration" && value >= 0) Capture(durations, tags);
        });
        listener.Start();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNoCtfObservability(builder.Configuration, "classification-test");
        await using var app = builder.Build();
        app.UseNoCtfObservability();
        app.MapGet("/api/tests/rest", () => "ok");
        app.MapPost("/api/tests/upload", () => Results.BadRequest())
            .WithMetadata(new ApiRequestMetricsMetadata(ApiRequestKind.Upload));
        app.MapGet("/api/tests/download", () => Results.StatusCode(500))
            .WithMetadata(new ApiRequestMetricsMetadata(ApiRequestKind.Download));
        app.MapGet("/hubs/tests", () => Results.StatusCode(500));
        app.MapGet("/api/tests/throw", (HttpContext _) => Task.FromException(new IOException("test")));
        app.MapGet("/_nuxt/tests.js", () => "asset");
        app.MapGet("/health/ready", () => "ready");
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var path in new[] { "/api/tests/rest", "/api/tests/download", "/hubs/tests", "/_nuxt/tests.js", "/health/ready" })
            using (await client.GetAsync(path)) { }
        using (await client.PostAsync("/api/tests/upload", null)) { }
        await Assert.That(async () => await client.GetAsync("/api/tests/throw")).Throws<IOException>();
        await Assert.That(counts.Count).IsEqualTo(5);
        await Assert.That(counts.OrderBy(x => x.Key)).IsEquivalentTo(durations.OrderBy(x => x.Key));
        await Assert.That(counts["/api/tests/rest"]).IsEqualTo("rest:success");
        await Assert.That(counts["/api/tests/upload"]).IsEqualTo("upload:client_error");
        await Assert.That(counts["/api/tests/download"]).IsEqualTo("download:server_error");
        await Assert.That(counts["/hubs/tests"]).IsEqualTo("signalr:server_error");
        await Assert.That(counts["/api/tests/throw"]).IsEqualTo("rest:server_error");
    }

    private static void Capture(ConcurrentDictionary<string, string> values, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string endpoint = "", kind = "", outcome = "";
        foreach (var tag in tags)
        {
            if (tag.Key == "endpoint") endpoint = tag.Value?.ToString() ?? "";
            if (tag.Key == "request_kind") kind = tag.Value?.ToString() ?? "";
            if (tag.Key == "outcome") outcome = tag.Value?.ToString() ?? "";
        }
        values[endpoint] = kind + ":" + outcome;
    }
}
