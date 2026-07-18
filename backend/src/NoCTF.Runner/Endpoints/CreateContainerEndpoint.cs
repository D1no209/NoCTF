using FastEndpoints;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class CreateContainerRequest
{
    public Guid OperationId { get; set; }
    public string Image { get; set; } = string.Empty;
    public List<string> Command { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<int, int> PortMappings { get; set; } = [];
}

public sealed class CreateContainerEndpoint(IContainerLifecycle lifecycle)
    : Endpoint<CreateContainerRequest, ContainerReceipt>
{
    public override void Configure() => Post("/containers");

    public override async Task HandleAsync(CreateContainerRequest request, CancellationToken cancellationToken)
    {
        var receipt = await lifecycle.CreateAsync(Create(request), cancellationToken);
        await HttpContext.Response.SendAsync<ContainerReceipt>(receipt, StatusCodes.Status201Created, null, cancellationToken);
    }

    private static ContainerRequest Create(CreateContainerRequest request) => new(
        request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId,
        request.Image,
        request.Command,
        request.Environment,
        request.Labels,
        request.PortMappings,
        new(268_435_456, 500_000_000, 128),
        new(true, false, true, ["ALL"], []),
        null);
}
