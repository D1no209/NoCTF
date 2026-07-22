using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.SystemProducers;

namespace NoCTF.Infrastructure.Runtime;

/// <summary>Delegates privileged container operations to the separately deployed Runner.</summary>
public sealed class RunnerContainerLifecycle(
    HttpClient client,
    IConfiguration configuration) : IContainerLifecycle, IOneShotJobRunner, IAwdFlagInjector
{
    private readonly string apiKey = configuration["Runtime:Runner:ApiKey"]
        ?? throw new InvalidOperationException("Runtime:Runner:ApiKey is required when Runner integration is enabled.");
    private readonly TimeSpan defaultRequestTimeout = TimeSpan.FromSeconds(
        Math.Clamp(configuration.GetValue("Runtime:Runner:TimeoutSeconds", 30), 1, 120));

    public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "containers")
        {
            Content = JsonContent.Create(new RunnerCreateContainerRequest(
                request.OperationId,
                request.Provider,
                request.Image,
                request.Command,
                request.Environment,
                request.Labels,
                request.PortMappings,
                request.Limits,
                request.Security,
                request.Ttl))
        };
        message.Headers.Add("X-Runner-Key", apiKey);
        using var response = await SendAsync(message, defaultRequestTimeout, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerReceipt>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Runner returned an empty container receipt.");
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "containers/destroy")
        {
            Content = JsonContent.Create(new RunnerDestroyContainerRequest(receipt.Provider, receipt.ResourceId))
        };
        message.Headers.Add("X-Runner-Key", apiKey);
        using var response = await SendAsync(message, defaultRequestTimeout, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ContainerReceipt?> GetAsync(
        NoCTF.Domain.Runtime.RuntimeProvider provider,
        string resourceId,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"containers/{Uri.EscapeDataString(resourceId)}?Provider={Uri.EscapeDataString(provider.ToString())}");
        message.Headers.Add("X-Runner-Key", apiKey);
        using var response = await SendAsync(message, defaultRequestTimeout, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerReceipt>(cancellationToken: cancellationToken);
    }

    public async Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "jobs/one-shot")
        {
            Content = JsonContent.Create(new RunnerOneShotRequest(
                request.OperationId,
                request.Provider,
                request.Image,
                request.Command,
                request.Environment,
                request.Labels,
                request.PortMappings,
                request.ScoringCallback,
                request.OperationTimeout is { } timeout
                    ? checked((int)Math.Ceiling(timeout.TotalSeconds))
                    : null))
        };
        message.Headers.Add("X-Runner-Key", apiKey);
        var requestTimeout = request.OperationTimeout is { } operationTimeout
            ? operationTimeout + RunnerScoringCallbackDeliveryPolicy.DispatchLeaseBuffer
            : defaultRequestTimeout;
        using var response = await SendAsync(message, requestTimeout, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OneShotResult>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Runner returned an empty one-shot result.");
    }

    public async Task<ContainerExecResult> InjectAsync(
        ContainerReceipt runtime,
        AwdFlagInjectionSettings settings,
        string flag,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "containers/awd-flag-injection")
        {
            Content = JsonContent.Create(new RunnerFlagInjectionRequest(
                runtime, settings.Command, flag, settings.TimeoutSeconds))
        };
        message.Headers.Add("X-Runner-Key", apiKey);
        using var response = await SendAsync(message,
            TimeSpan.FromSeconds(settings.TimeoutSeconds) + defaultRequestTimeout + TimeSpan.FromSeconds(10),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerExecResult>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Runner returned an empty AWD flag injection result.");
    }

    private sealed record RunnerCreateContainerRequest(
        Guid OperationId,
        NoCTF.Domain.Runtime.RuntimeProvider Provider,
        string Image,
        IReadOnlyList<string> Command,
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyDictionary<int, int> PortMappings,
        ContainerResourceLimits Limits,
        ContainerSecurityPolicy Security,
        TimeSpan? Ttl);

    private sealed record RunnerDestroyContainerRequest(
        NoCTF.Domain.Runtime.RuntimeProvider Provider,
        string ResourceId);

    private sealed record RunnerOneShotRequest(
        Guid OperationId,
        NoCTF.Domain.Runtime.RuntimeProvider Provider,
        string Image,
        IReadOnlyList<string> Command,
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyDictionary<int, int> PortMappings,
        RunnerScoringCallback? ScoringCallback,
        int? TimeoutSeconds);

    private sealed record RunnerFlagInjectionRequest(
        ContainerReceipt Runtime,
        IReadOnlyList<string> Command,
        string ProtectedInput,
        int TimeoutSeconds);

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return await client.SendAsync(message, timeoutSource.Token);
    }
}
