using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Endpoints;

public sealed class ComposeDownRequest
{
    public Guid OperationId { get; set; }
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
    public string ProjectName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
}

public sealed class ComposeDownEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<ComposeDownRequest, Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/compose/down");
        AllowAnonymous(); // Authentication is enforced by RunnerSecurity's API-key middleware.
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(
        ComposeDownRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await providers.Compose(request.Provider).DownAsync(new(
                request.OperationId,
                request.Provider,
                request.ProjectName,
                request.Namespace,
                DateTimeOffset.UtcNow), cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }
}
