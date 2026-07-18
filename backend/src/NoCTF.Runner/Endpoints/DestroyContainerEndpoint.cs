using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Endpoints;

public sealed class DestroyContainerRequest
{
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
    public string ResourceId { get; set; } = string.Empty;
}

public sealed class DestroyContainerEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<DestroyContainerRequest, Results<NoContent, ProblemHttpResult>>
{
    public override void Configure() => Post("/containers/destroy");

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(
        DestroyContainerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await providers.Containers(request.Provider).DestroyAsync(
                new(Guid.Empty, request.Provider, request.ResourceId, RuntimeStatus.Running, new Dictionary<int, int>(), null, null),
                cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }
}
