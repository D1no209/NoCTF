using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Endpoints;

public sealed class RunOneShotEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<CreateContainerRequest, Results<Ok<OneShotResult>, ProblemHttpResult>>
{
    public override void Configure() => Post("/jobs/one-shot");

    public override async Task<Results<Ok<OneShotResult>, ProblemHttpResult>> ExecuteAsync(
        CreateContainerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await providers.OneShot(request.Provider).RunAsync(new(
                request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId,
                request.Image,
                request.Command,
                request.Environment,
                request.Labels,
                request.PortMappings,
                new(268_435_456, 500_000_000, 128),
                new(true, false, true, ["ALL"], []),
                null), cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }
}
