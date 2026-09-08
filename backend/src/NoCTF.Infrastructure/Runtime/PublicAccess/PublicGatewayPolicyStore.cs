using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using ZiggyCreatures.Caching.Fusion;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.Runtime.PublicAccess;

public sealed class PublicGatewayPolicyStore(NoCtfDbContext db, IFusionCacheProvider caches,
    ITransactionalMessageOutbox outbox, PublicGatewayCapability? capability = null,
    PostCommitDispatchStatus? dispatch = null, ILogger<PublicGatewayPolicyStore>? logger = null) : IPublicGatewayPolicyStore
{
    private const string Key = "public-gateway-policy";
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.ReadModels);

    public Task<PublicGatewayPolicy> GetAsync(CancellationToken ct) => cache.GetOrSetAsync<PublicGatewayPolicy>(Key,
        async (_, token) =>
        {
            var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(x => x.Id == 1, token);
            return new(settings.PublicGatewayEnabled, settings.PublicGatewayConnectorId, settings.PublicGatewayOrigin,
                settings.PublicGatewayDirectOrigins, settings.PublicGatewayRuntimeHost, settings.PublicGatewayDirectHostOverride,
                settings.PublicGatewayMaxPorts);
        }, token: ct).AsTask();

    public async Task SaveAsync(PublicGatewayPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var settings = await db.PlatformSettings.SingleAsync(x => x.Id == 1, ct);
        settings.PublicGatewayEnabled = policy.Enabled;
        settings.PublicGatewayConnectorId = policy.ConnectorId;
        settings.PublicGatewayOrigin = policy.PublicOrigin;
        settings.PublicGatewayDirectOrigins = policy.DirectOrigins.ToArray();
        settings.PublicGatewayRuntimeHost = policy.PublicRuntimeHost;
        settings.PublicGatewayDirectHostOverride = policy.DirectRuntimeHostOverride;
        settings.PublicGatewayMaxPorts = policy.MaxPublishedPorts;
        settings.UpdatedAt = now;
        if (capability is not null)
            await outbox.PublishToRunnerNodeAsync(new ReconcilePublicGateway(capability.RunnerId));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        try { await InvalidateAsync(ct); }
        catch (Exception exception)
        {
            // The committed node message retries cache invalidation before waking its controller.
            dispatch?.MarkPending();
            logger?.LogWarning("Gateway policy committed; cache invalidation pending after {FailureType}.", exception.GetType().Name);
        }
        await outbox.FlushCommittedMessagesAsync();
    }
    public Task InvalidateAsync(CancellationToken ct) => cache.RemoveAsync(Key, token: ct).AsTask();
}
