using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Infrastructure.Runtime;

/// <summary>Delegates privileged container operations to the separately deployed Runner.</summary>
public sealed class RunnerContainerLifecycle(
    HttpClient client,
    IConfiguration configuration) : IContainerLifecycle
{
    private readonly string apiKey = configuration["Runtime:Runner:ApiKey"]
        ?? throw new InvalidOperationException("Runtime:Runner:ApiKey is required when Runner integration is enabled.");

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
        using var response = await client.SendAsync(message, cancellationToken);
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
        using var response = await client.SendAsync(message, cancellationToken);
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
        using var response = await client.SendAsync(message, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerReceipt>(cancellationToken: cancellationToken);
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
}
