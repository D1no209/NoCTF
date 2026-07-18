using FastEndpoints;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class ComposeUpRequest
{
    public Guid OperationId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ComposeYaml { get; set; } = string.Empty;
    public Dictionary<string, string> Environment { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public sealed class ComposeUpEndpoint(IComposeRuntime runtime) : Endpoint<ComposeUpRequest, ComposeReceipt>
{
    public override void Configure() => Post("/compose/up");

    public override async Task HandleAsync(ComposeUpRequest request, CancellationToken cancellationToken)
    {
        var result = await runtime.UpAsync(new(
            request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId,
            request.ProjectName,
            request.ComposeYaml,
            request.Environment,
            request.Labels,
            null), cancellationToken);
        await HttpContext.Response.SendAsync<ComposeReceipt>(result, StatusCodes.Status201Created, null, cancellationToken);
    }
}
