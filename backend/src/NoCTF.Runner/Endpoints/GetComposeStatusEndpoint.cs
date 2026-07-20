using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Endpoints;

public sealed class GetComposeStatusRequest
{
    public Guid OperationId { get; set; }
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
    public string ProjectName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
}

public sealed class GetComposeStatusEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<GetComposeStatusRequest, Results<Ok<ComposeStatus>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/compose/status");
        AllowAnonymous(); // Authentication is enforced by RunnerSecurity's API-key middleware.
    }

    public override async Task<Results<Ok<ComposeStatus>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetComposeStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await providers.Compose(request.Provider).GetStatusAsync(new(
                request.OperationId,
                request.Provider,
                request.ProjectName,
                request.Namespace,
                DateTimeOffset.UtcNow), cancellationToken);
            return status is null ? TypedResults.NotFound() : TypedResults.Ok(status);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }
}
