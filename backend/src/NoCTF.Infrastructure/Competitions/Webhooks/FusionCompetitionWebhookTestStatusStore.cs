using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

/// <summary>Short-lived presentation state; delivery itself remains a durable message.</summary>
public sealed class FusionCompetitionWebhookTestStatusStore(IFusionCacheProvider caches)
    : ICompetitionWebhookTestStatusStore
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.WebhookTestStatuses);

    public async Task CreateAsync(
        CompetitionWebhookTestStatus status, CancellationToken cancellationToken)
    {
        var key = Key(status.DeliveryId);
        if (await cache.GetOrDefaultAsync<CompetitionWebhookTestStatus?>(
                key, null, token: cancellationToken) is not null)
            throw new InvalidOperationException("Webhook test delivery already exists.");
        await cache.SetAsync(key, status, token: cancellationToken);
    }

    public async Task<CompetitionWebhookTestStatus?> GetAsync(
        Guid deliveryId, CancellationToken cancellationToken) =>
        await cache.GetOrDefaultAsync<CompetitionWebhookTestStatus?>(
            Key(deliveryId), null, token: cancellationToken);

    public async Task CompleteAsync(
        Guid deliveryId, CompetitionWebhookTestState state,
        DateTimeOffset completedAt, CompetitionWebhookTestFailureCode? failureCode,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(deliveryId, cancellationToken);
        if (current is null || current.CompletedAt is not null)
            return;
        await cache.SetAsync(Key(deliveryId), current with
        {
            State = state,
            CompletedAt = completedAt,
            FailureCode = failureCode
        }, token: cancellationToken);
    }

    private static string Key(Guid id) => id.ToString("N");
}
