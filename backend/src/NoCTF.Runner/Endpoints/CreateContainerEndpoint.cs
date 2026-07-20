using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Endpoints;

public sealed class CreateContainerRequest
{
    public Guid OperationId { get; set; }
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
    public string Image { get; set; } = string.Empty;
    public List<string> Command { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<int, int> PortMappings { get; set; } = [];
    public ContainerResourceLimits? Limits { get; set; }
    public ContainerSecurityPolicy? Security { get; set; }
    public TimeSpan? Ttl { get; set; }
}

public sealed class CreateContainerEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<CreateContainerRequest, Results<Created<ContainerReceipt>, ProblemHttpResult>>
{
    public override void Configure() => Post("/containers");

    public override async Task<Results<Created<ContainerReceipt>, ProblemHttpResult>> ExecuteAsync(
        CreateContainerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await providers.Containers(request.Provider).CreateAsync(Create(request), cancellationToken);
            return TypedResults.Created($"/containers/{receipt.ResourceId}", receipt);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }

    private static ContainerRequest Create(CreateContainerRequest request) => new(
        request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId,
        request.Provider,
        request.Image,
        request.Command,
        request.Environment,
        request.Labels,
        request.PortMappings,
        request.Limits ?? new(268_435_456, 500_000_000, 128),
        request.Security ?? new(true, false, true, ["ALL"], []),
        request.Ttl);
}
