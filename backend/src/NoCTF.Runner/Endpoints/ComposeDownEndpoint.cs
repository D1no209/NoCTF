using FastEndpoints;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class ComposeDownRequest
{
    public Guid OperationId { get; set; }
    public string Provider { get; set; } = "docker";
    public string ProjectName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
}

public sealed class ComposeDownEndpoint(IComposeRuntime runtime) : Endpoint<ComposeDownRequest>
{
    public override void Configure() => Post("/compose/down");

    public override async Task HandleAsync(ComposeDownRequest request, CancellationToken cancellationToken)
    {
        await runtime.DownAsync(new(
            request.OperationId,
            request.Provider,
            request.ProjectName,
            request.Namespace,
            DateTimeOffset.UtcNow), cancellationToken);
        await HttpContext.Response.SendNoContentAsync(cancellationToken);
    }
}
