using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Runtime.PublicAccess;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PublicGatewayPolicyResponse(bool Enabled, string ConnectorId, string PublicOrigin,
    IReadOnlyList<string> DirectOrigins, string PublicRuntimeHost, string? DirectRuntimeHostOverride, int MaxPublishedPorts);
public sealed record PublicGatewayCapabilityResponse(string ConnectorId, string RunnerId,
    IReadOnlyList<string> ApprovedOrigins, int FirstPort, int LastPort, IReadOnlyList<int> ReservedPorts,
    int MaximumPorts, bool NamespaceIsolationAvailable, string? ConfigurationError);
public sealed record PublicGatewayConfigurationResponse(PublicGatewayPolicyResponse Policy, PublicGatewayCapabilityResponse? Capability);

internal static class PublicGatewayConfigurationMapping
{
    public static PublicGatewayConfigurationResponse ToResponse(PublicGatewayConfiguration value) => new(
        new(value.Policy.Enabled, value.Policy.ConnectorId, value.Policy.PublicOrigin, value.Policy.DirectOrigins,
            value.Policy.PublicRuntimeHost, value.Policy.DirectRuntimeHostOverride, value.Policy.MaxPublishedPorts),
        value.Capability is not { } capability ? null : new(capability.ConnectorId, capability.RunnerId,
            capability.ApprovedOrigins, capability.FirstPort, capability.LastPort, capability.ReservedPorts,
            capability.MaximumPorts, capability.NamespaceIsolationAvailable, capability.ConfigurationError));
}
