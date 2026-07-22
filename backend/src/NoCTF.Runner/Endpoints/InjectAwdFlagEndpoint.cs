using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Endpoints;

public sealed class InjectAwdFlagRequest
{
    public ContainerReceipt Runtime { get; set; } = default!;
    public List<string> Command { get; set; } = [];
    public string ProtectedInput { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class InjectAwdFlagEndpoint(IContainerRuntimeProviderCatalog providers)
    : Endpoint<InjectAwdFlagRequest, Results<Ok<ContainerExecResult>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/containers/awd-flag-injection");
        AllowAnonymous(); // Runner API-key middleware authenticates this internal operation.
    }

    public override async Task<Results<Ok<ContainerExecResult>, ProblemHttpResult>> ExecuteAsync(
        InjectAwdFlagRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Runtime is null || request.Command.Count == 0 || request.Command.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrEmpty(request.ProtectedInput) || request.TimeoutSeconds <= 0)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid AWD flag injection request.");
        try
        {
            var lifecycle = providers.Containers(request.Runtime.Provider) as IContainerSandboxLifecycle
                ?? throw new UnsupportedRuntimeProviderException(request.Runtime.Provider);
            var input = Encoding.UTF8.GetBytes(request.ProtectedInput + "\n");
            var result = await lifecycle.ExecWithInputAsync(request.Runtime, request.Command, input,
                TimeSpan.FromSeconds(request.TimeoutSeconds), cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "AWD flag injection failed.");
        }
    }
}
