using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class FusionCacheMetricsAgentTests
{
    [Test]
    public async Task Named_cache_reads_emit_low_cardinality_hit_and_miss_metrics(
        CancellationToken ct)
    {
        var observed = new ConcurrentQueue<(string Cache, string Tier, string Result)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                    && instrument.Name == "noctf.fusion_cache.reads")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string cache = "", tier = "", result = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "cache") cache = tag.Value?.ToString() ?? "";
                if (tag.Key == "tier") tier = tag.Value?.ToString() ?? "";
                if (tag.Key == "result") result = tag.Value?.ToString() ?? "";
            }
            observed.Enqueue((cache, tier, result));
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNoCtfCaching(new ConfigurationBuilder().Build(), development: true);
        using var provider = services.BuildServiceProvider();
        var agent = new FusionCacheMetricsAgent(
            provider.GetRequiredService<IFusionCacheProvider>());
        await agent.StartAsync(ct);
        try
        {
            var cache = provider.GetRequiredService<IFusionCacheProvider>()
                .GetCache(NoCtfCacheNames.ReadModels);
            var key = "metrics-test:" + Guid.NewGuid().ToString("N");
            _ = await cache.GetOrDefaultAsync<string>(key, null, token: ct);
            await cache.SetAsync(key, "value", token: ct);
            _ = await cache.GetOrDefaultAsync<string>(key, null, token: ct);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline
                && (!observed.Any(item => item ==
                        (NoCtfCacheNames.ReadModels, "memory", "miss"))
                    || !observed.Any(item => item ==
                        (NoCtfCacheNames.ReadModels, "memory", "hit"))))
                await Task.Delay(10, ct);
        }
        finally
        {
            await agent.StopAsync(ct);
        }

        await Assert.That(observed.Any(item => item ==
            (NoCtfCacheNames.ReadModels, "memory", "miss"))).IsTrue();
        await Assert.That(observed.Any(item => item ==
            (NoCtfCacheNames.ReadModels, "memory", "hit"))).IsTrue();
    }
}
