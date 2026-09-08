using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Infrastructure.Caching;
using NoCTF.Domain.Platform;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Runtime.PublicAccess;

public static class PublicGatewayInfrastructure
{
    public static IServiceCollection AddNoCtfPublicGateway(this IServiceCollection services, IConfiguration configuration, bool standaloneRunner = false)
    {
        if (services.Any(item => item.ServiceType == typeof(PublicGatewayRegistration))) return services;
        services.AddSingleton<PublicGatewayRegistration>();
        if (standaloneRunner)
            services.AddNoCtfCaching(configuration, development: false);
        var connectorId = configuration["PublicGateway:ConnectorId"];
        if (!string.IsNullOrWhiteSpace(connectorId))
        {
            var runnerId = configuration["PublicGateway:RunnerId"];
            var origins = configuration.GetSection("PublicGateway:ApprovedOrigins").Get<string[]>() ?? [];
            var firstPort = configuration.GetValue("PublicGateway:FirstPort", 32768);
            var lastPort = configuration.GetValue("PublicGateway:LastPort", 60997);
            var maximumPorts = configuration.GetValue("PublicGateway:MaximumPorts", 8);
            var knownTransport = Enum.TryParse<PublicGatewayTransportKind>(configuration["PublicGateway:Transport"] ?? "IsolatedFrp", ignoreCase: false, out var transport)
                && Enum.IsDefined(transport);
            var reserved = configuration.GetSection("PublicGateway:ReservedPorts").Get<int[]>() ?? [];
            if (transport == PublicGatewayTransportKind.SharedSsh)
                reserved = reserved.Concat([36632, 60998, configuration.GetValue("PublicGateway:ServerPort", 60999)]).Distinct().ToArray();
            var docker = string.Equals(configuration["Runtime:Provider"] ?? configuration["Runner:Provider"] ?? "Docker", "Docker", StringComparison.OrdinalIgnoreCase);
            var valid = knownTransport && !(connectorId.Length > 128 || string.IsNullOrWhiteSpace(runnerId) || runnerId.Length > 128
                || origins.Length is 0 or > 16 || origins.Any(origin => PublicGatewayPolicyRules.Origin(origin, true) is null)
                || firstPort < 1024 || lastPort > 65535 || firstPort > lastPort || maximumPorts is < 1 or > 64
                || transport == PublicGatewayTransportKind.SharedSsh && (firstPort < 32768 || lastPort > 60999 || lastPort - firstPort + 1 > 256
                    || lastPort - firstPort + 1 - reserved.Count(port => port >= firstPort && port <= lastPort) < maximumPorts));
            services.AddSingleton(new PublicGatewayCapability(connectorId, runnerId ?? "", origins, firstPort, lastPort,
                reserved, maximumPorts,
                valid && docker && configuration.GetValue("PublicGateway:NamespaceIsolationAvailable", false),
                !valid ? "PublicGateway deployment capabilities are invalid." : !docker ? "Public gateway requires the Docker runtime provider." : null, transport));
        }
        services.AddScoped<IPublicGatewayPolicyStore, PublicGatewayPolicyStore>();
        services.AddScoped<ManagePublicGateway>();
        services.AddSingleton<IPublicGatewayStatusStore, PublicGatewayStatusStore>();
        services.AddScoped<ReadRuntimePublicAccess>();
        services.AddScoped<PublicGatewayLeaseGuard>();
        return services;
    }
    private sealed class PublicGatewayRegistration;
}

public sealed class PublicGatewayStatusStore(IFusionCacheProvider caches, TimeProvider clock) : IPublicGatewayStatusStore
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.ReadModels);
    private static string Key(Guid runtimeId) => $"public-gateway-runtime:{runtimeId:N}";
    public async Task<PublicRuntimeStatus?> GetAsync(Guid runtimeId, CancellationToken ct)
    {
        var status = await cache.GetOrDefaultAsync<PublicRuntimeStatus?>(Key(runtimeId), null, token: ct);
        return status?.ValidUntil > clock.GetUtcNow() ? status : null;
    }
    public async Task SetAsync(PublicRuntimeStatus status, CancellationToken ct)
    {
        var remaining = status.ValidUntil - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) { await RemoveAsync(status.RuntimeId, ct); return; }
        await cache.SetAsync(Key(status.RuntimeId), status, options: new FusionCacheEntryOptions
        {
            Duration = remaining > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : remaining,
            IsFailSafeEnabled = false
        }, token: ct);
    }
    public Task RemoveAsync(Guid runtimeId, CancellationToken ct) => cache.RemoveAsync(Key(runtimeId), token: ct).AsTask();
    public async Task<PublicGatewayConnectorStatus?> GetConnectorAsync(string connectorId, CancellationToken ct)
    {
        var status = await cache.GetOrDefaultAsync<PublicGatewayConnectorStatus?>("public-gateway-connector:" + connectorId, null, token: ct);
        return status?.ValidUntil > clock.GetUtcNow() ? status : null;
    }
    public Task SetConnectorAsync(PublicGatewayConnectorStatus status, CancellationToken ct) =>
        cache.SetAsync("public-gateway-connector:" + status.ConnectorId, status,
            options: new FusionCacheEntryOptions { Duration = status.ValidUntil == DateTimeOffset.MaxValue ? TimeSpan.FromDays(3650) : TimeSpan.FromSeconds(10), IsFailSafeEnabled = false }, token: ct).AsTask();
}
