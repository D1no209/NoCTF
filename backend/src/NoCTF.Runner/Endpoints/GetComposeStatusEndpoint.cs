using FastEndpoints;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class GetComposeStatusRequest
{
    public Guid OperationId { get; set; }
    public string Provider { get; set; } = "docker";
    public string ProjectName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
}

public sealed class GetComposeStatusEndpoint(IComposeRuntime runtime) : Endpoint<GetComposeStatusRequest, ComposeStatus>
{
    public override void Configure() => Post("/compose/status");

    public override async Task HandleAsync(GetComposeStatusRequest request, CancellationToken cancellationToken)
    {
        var status = await runtime.GetStatusAsync(new(
            request.OperationId,
            request.Provider,
            request.ProjectName,
            request.Namespace,
            DateTimeOffset.UtcNow), cancellationToken);
        if (status is null)
        {
            await HttpContext.Response.SendNotFoundAsync(cancellationToken);
            return;
        }
        await HttpContext.Response.SendAsync<ComposeStatus>(status, StatusCodes.Status200OK, null, cancellationToken);
    }
}
