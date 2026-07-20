using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Endpoints;

public sealed class ComposeUpRequest
{
    public Guid OperationId { get; set; }
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
    public string ProjectName { get; set; } = string.Empty;
    public string ComposeYaml { get; set; } = string.Empty;
    public Dictionary<string, string> Environment { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public sealed class ComposeUpEndpoint(RuntimeProviderCatalog providers)
    : Endpoint<ComposeUpRequest, Results<Created<ComposeReceipt>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/compose/up");
        AllowAnonymous(); // Authentication is enforced by RunnerSecurity's API-key middleware.
    }

    public override async Task<Results<Created<ComposeReceipt>, ProblemHttpResult>> ExecuteAsync(
        ComposeUpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await providers.Compose(request.Provider).UpAsync(new(
                request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId,
                request.ProjectName,
                request.ComposeYaml,
                request.Environment,
                request.Labels,
                null), cancellationToken);
            return TypedResults.Created($"/compose/status?operationId={result.OperationId}", result);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }
}
