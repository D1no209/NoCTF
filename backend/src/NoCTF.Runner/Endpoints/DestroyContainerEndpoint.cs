using FastEndpoints;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class DestroyContainerRequest
{
    public string Provider { get; set; } = "docker";
    public string ResourceId { get; set; } = string.Empty;
}

public sealed class DestroyContainerEndpoint(IContainerLifecycle lifecycle) : Endpoint<DestroyContainerRequest>
{
    public override void Configure() => Post("/containers/destroy");

    public override async Task HandleAsync(DestroyContainerRequest request, CancellationToken cancellationToken)
    {
        await lifecycle.DestroyAsync(new(Guid.Empty, request.Provider, request.ResourceId, "running", new Dictionary<int, int>(), null, null), cancellationToken);
        await HttpContext.Response.SendNoContentAsync(cancellationToken);
    }
}
