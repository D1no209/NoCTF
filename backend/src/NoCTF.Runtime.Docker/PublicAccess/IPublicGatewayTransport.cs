using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.PublicAccess;

namespace NoCTF.Runtime.Docker.PublicAccess;

/// <summary>Optional public data-plane lifecycle; implementations never change the original Runtime.</summary>
public interface IPublicGatewayTransport
{
    bool UsesIndependentPublicPorts { get; }
    bool RequiresReset => false;
    Task<GatewayPublication> StartAsync(Guid runtimeId, string targetId, IReadOnlyList<RuntimePublishedPortView> ports, CancellationToken ct);
    Task RenewAsync(GatewayPublication publication, DateTimeOffset expiresAt, CancellationToken ct);
    Task<IReadOnlyList<PublicEndpointStatus>> ReadStatusAsync(GatewayPublication publication, CancellationToken ct);
    Task RevokeAsync(GatewayPublication publication, CancellationToken ct);
    Task RevokeStaleHelpersAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
