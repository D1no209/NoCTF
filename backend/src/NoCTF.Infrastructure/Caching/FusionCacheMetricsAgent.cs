using Microsoft.Extensions.Hosting;
using NoCTF.Application.Observability;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace NoCTF.Infrastructure.Caching;

public sealed class FusionCacheMetricsAgent(IFusionCacheProvider caches) : IHostedService
{
    private readonly List<Action> unsubscribe = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[]
        {
            NoCtfCacheNames.Leaderboards,
            NoCtfCacheNames.ReadModels,
            NoCtfCacheNames.WebhookTestStatuses,
            NoCtfCacheNames.LocalComputation
        })
        {
            var cache = caches.GetCache(name);
            EventHandler<FusionCacheEntryHitEventArgs> memoryHit = (_, _) =>
                NoCtfTelemetry.RecordFusionCacheRead(name, "memory", true);
            EventHandler<FusionCacheEntryEventArgs> memoryMiss = (_, _) =>
                NoCtfTelemetry.RecordFusionCacheRead(name, "memory", false);
            EventHandler<FusionCacheEntryHitEventArgs> distributedHit = (_, _) =>
                NoCtfTelemetry.RecordFusionCacheRead(name, "distributed", true);
            EventHandler<FusionCacheEntryEventArgs> distributedMiss = (_, _) =>
                NoCtfTelemetry.RecordFusionCacheRead(name, "distributed", false);
            cache.Events.Memory.Hit += memoryHit;
            cache.Events.Memory.Miss += memoryMiss;
            cache.Events.Distributed.Hit += distributedHit;
            cache.Events.Distributed.Miss += distributedMiss;
            unsubscribe.Add(() =>
            {
                cache.Events.Memory.Hit -= memoryHit;
                cache.Events.Memory.Miss -= memoryMiss;
                cache.Events.Distributed.Hit -= distributedHit;
                cache.Events.Distributed.Miss -= distributedMiss;
            });
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var stop in unsubscribe)
            stop();
        unsubscribe.Clear();
        return Task.CompletedTask;
    }
}
