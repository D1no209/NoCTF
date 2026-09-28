using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Runtime.Access;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class ProbeRuntimeProxyRequest
{
    public Guid RuntimeInstanceId { get; set; }
    public int BindingIndex { get; set; }
}

public sealed class ProbeRuntimeProxyEndpoint(
    IRuntimeProxyTargetReader targets)
    : Endpoint<ProbeRuntimeProxyRequest, Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Get("/runtime-proxies/{runtimeInstanceId}/{bindingIndex}");
        AllowAnonymous();
        Description(builder => builder.WithName("ProbeRuntimeProxy"));
        Summary(summary =>
        {
            summary.Summary = "Probes a Runtime TCP-over-WebSocket target.";
            summary.Description =
                "A valid running target returns no content; WebSocket upgrades are handled by the Runtime proxy transport.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(
        ProbeRuntimeProxyRequest request,
        CancellationToken cancellationToken)
    {
        var target = await targets.FindAsync(
            request.RuntimeInstanceId,
            request.BindingIndex,
            cancellationToken);
        return target is null
            ? TypedResults.NotFound()
            : TypedResults.NoContent();
    }
}
