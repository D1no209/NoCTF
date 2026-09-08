using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.Application.Runtime.PublicAccess;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PublicGatewayRuntimeStatusResponse(Guid RuntimeId, DateTimeOffset ValidUntil,
    IReadOnlyList<PublicEndpointResponse> Endpoints, PublicAccessFailureProtocol? Failure);
public sealed record PublicGatewayStatusResponse(bool Configured, bool Enabled, bool Applied,
    DateTimeOffset? ValidUntil, PublicAccessFailureProtocol? Failure,
    IReadOnlyList<PublicGatewayRuntimeStatusResponse> Runtimes);

public sealed class GetPublicGatewayStatusEndpoint(ManagePublicGateway gateway, IPublicGatewayStatusStore statuses)
    : EndpointWithoutRequest<Ok<PublicGatewayStatusResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/public-gateway/status");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetPublicGatewayStatus"));
        Summary(summary =>
        {
            summary.Summary = "Reads current public gateway application status.";
            summary.Description = "Reads bounded connector status from cache without contacting Docker, FRP or external hosts.";
        });
    }
    public override async Task<Ok<PublicGatewayStatusResponse>> ExecuteAsync(CancellationToken ct)
    {
        var configuration = await gateway.GetAsync(ct);
        var status = configuration.Capability is null ? null
            : await statuses.GetConnectorAsync(configuration.Capability.ConnectorId, ct);
        return TypedResults.Ok(new PublicGatewayStatusResponse(configuration.Capability is not null,
            configuration.Policy.Enabled, status is not null && status.PolicyHash == configuration.Policy.Fingerprint() && status.Failure is null
                && status.Runtimes.All(runtime => runtime.Failure is null && runtime.Endpoints.Count > 0 && runtime.Endpoints.All(endpoint => endpoint.State == NoCTF.Domain.Platform.PublicAccessState.Ready)),
            status?.ValidUntil,
            status?.Failure is { } failure ? RuntimeAccessMapping.ToProtocol(failure)
                : status is null && configuration.Capability is not null ? PublicAccessFailureProtocol.ConnectorOffline : null,
            status?.Runtimes.Select(runtime => new PublicGatewayRuntimeStatusResponse(runtime.RuntimeId, runtime.ValidUntil,
                runtime.Endpoints.Select(item => new PublicEndpointResponse(item.ContainerPort, item.HostPort,
                    RuntimeAccessMapping.ToProtocol(item.State), item.Failure is { } code ? RuntimeAccessMapping.ToProtocol(code) : null, item.PublicPort)).ToArray(),
                runtime.Failure is { } runtimeFailure ? RuntimeAccessMapping.ToProtocol(runtimeFailure) : null)).ToArray() ?? []));
    }
}
