using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Endpoints;

public sealed class GetContainerRequest
{
    public string ResourceId { get; set; } = string.Empty;
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
}

public sealed class GetContainerEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<GetContainerRequest, Results<Ok<ContainerReceipt>, NotFound, ProblemHttpResult>>
{
    public override void Configure() => Get("/containers/{resourceId}");

    public override async Task<Results<Ok<ContainerReceipt>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetContainerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await providers.Containers(request.Provider).GetAsync(request.ResourceId, cancellationToken);
            return receipt is null ? TypedResults.NotFound() : TypedResults.Ok(receipt);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }
}
