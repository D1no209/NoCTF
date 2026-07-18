using FastEndpoints;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class RunOneShotEndpoint(IOneShotJobRunner runner)
    : Endpoint<CreateContainerRequest, OneShotResult>
{
    public override void Configure() => Post("/jobs/one-shot");

    public override async Task HandleAsync(CreateContainerRequest request, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new(
            request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId,
            request.Image,
            request.Command,
            request.Environment,
            request.Labels,
            request.PortMappings,
            new(268_435_456, 500_000_000, 128),
            new(true, false, true, ["ALL"], []),
            null), cancellationToken);
        await HttpContext.Response.SendAsync<OneShotResult>(result, StatusCodes.Status200OK, null, cancellationToken);
    }
}
