using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Endpoints;

public sealed class RunOneShotRequest
{
    public Guid OperationId { get; set; }
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;
    public string Image { get; set; } = string.Empty;
    public List<string> Command { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<int, int> PortMappings { get; set; } = [];
    public RunnerScoringCallback? ScoringCallback { get; set; }
    public int? TimeoutSeconds { get; set; }
}

public sealed class RunOneShotEndpoint(
    IOneShotRuntimeProviderCatalog providers,
    IRunnerScoringCallbackDispatcher callbacks)
    : Endpoint<RunOneShotRequest, Results<Ok<OneShotResult>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/jobs/one-shot");
        AllowAnonymous(); // Authentication is enforced by RunnerSecurity's API-key middleware.
    }

    public override async Task<Results<Ok<OneShotResult>, ProblemHttpResult>> ExecuteAsync(
        RunOneShotRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TimeoutSeconds is <= 0)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Runner job timeout must be positive.");
        var operationId = request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId;
        using var timeoutSource = request.TimeoutSeconds is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutSource?.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds.GetValueOrDefault()));
        OneShotResult result;
        try
        {
            result = await providers.OneShot(request.Provider).RunAsync(new(
                operationId,
                request.Provider,
                request.Image,
                request.Command,
                request.Environment,
                request.Labels,
                request.PortMappings,
                new(268_435_456, 500_000_000, 128),
                new(true, false, true, ["ALL"], []),
                null), timeoutSource?.Token ?? cancellationToken);
        }
        catch (UnsupportedRuntimeProviderException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (timeoutSource?.IsCancellationRequested == true)
        {
            var now = DateTimeOffset.UtcNow;
            result = new OneShotResult(operationId.ToString("N"), -1, string.Empty, string.Empty, now, now);
            try
            {
                await callbacks.DispatchAsync(request.ScoringCallback, result, true, cancellationToken);
                return TypedResults.Ok(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "Runner scoring callback failed.");
            }
        }
        catch
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Runner job failed.");
        }

        try
        {
            await callbacks.DispatchAsync(request.ScoringCallback, result, false, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Runner scoring callback failed.");
        }
    }

}
