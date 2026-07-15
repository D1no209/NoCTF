using System.Net.Http.Json;
using NoCTF.PluginBase;

namespace NoCTF.Runner.Client;

public interface IRunnerClient
{
    Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default);
    Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default);
    Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default);
    Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default);
    Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default);
    Task<ComposeStatus> GetComposeStatusAsync(string projectName, Dictionary<string, string>? labels = null, CancellationToken ct = default);
}

public class HttpRunnerClient(HttpClient httpClient) : IRunnerClient
{
    public async Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
    {
        config = EnsureOperationId(config);
        using var response = await httpClient.PostAsJsonAsync("/runner/containers", config, ct);
        response.EnsureSuccessStatusCode();
        var container = await response.Content.ReadFromJsonAsync<ContainerInstance>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty container response.");
        return NormalizeRunnerLocalEndpoint(container);
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/runner/containers/destroy", container, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
    {
        config = EnsureOperationId(config);
        using var response = await httpClient.PostAsJsonAsync("/runner/jobs/one-shot", config, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerRunResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty job response.");
    }

    public async Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
    {
        config = EnsureOperationId(config);
        using var response = await httpClient.PostAsJsonAsync("/runner/compose/up", config, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ComposeDeployment>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty compose response.");
    }

    public async Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/runner/compose/down", deployment, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ComposeStatus> GetComposeStatusAsync(string projectName, Dictionary<string, string>? labels = null, CancellationToken ct = default)
    {
        var query = labels is null || labels.Count == 0
            ? string.Empty
            : "?" + string.Join("&", labels.Select(kvp =>
                $"label={Uri.EscapeDataString($"{kvp.Key}={kvp.Value}")}"));
        using var response = await httpClient.GetAsync($"/runner/compose/{Uri.EscapeDataString(projectName)}/status{query}", ct);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<ComposeStatus>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty compose status response.");
        return status with
        {
            Services = status.Services
                .Select(service => service with
                {
                    InternalHost = NormalizeRunnerLocalHost(service.InternalHost)
                })
                .ToArray()
        };
    }

    private static ContainerConfig EnsureOperationId(ContainerConfig config)
        => config.OperationId.HasValue
            ? config
            : config with { OperationId = Guid.NewGuid() };

    private static ComposeConfig EnsureOperationId(ComposeConfig config)
        => config.OperationId.HasValue
            ? config
            : config with { OperationId = Guid.NewGuid() };

    private ContainerInstance NormalizeRunnerLocalEndpoint(ContainerInstance container)
        => container with { InternalHost = NormalizeRunnerLocalHost(container.InternalHost) };

    private string? NormalizeRunnerLocalHost(string? host)
    {
        if (!IsLoopbackHost(host))
            return host;

        // Docker reports published ports as reachable on its own loopback. When
        // Runner is remote, the caller must use Runner's host with those ports.
        return httpClient.BaseAddress?.Host ?? host;
    }

    private static bool IsLoopbackHost(string? host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
           string.Equals(host, "::1", StringComparison.Ordinal);
}

public class RunnerBackedContainerManager(IRunnerClient runnerClient) : IContainerManager
{
    public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
        => runnerClient.CreateContainerAsync(config, cancellationToken);

    public Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
        => runnerClient.DestroyContainerAsync(container, cancellationToken);

    public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
        => runnerClient.RunContainerAsync(config, cancellationToken);

    public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
        => runnerClient.ComposeUpAsync(config, cancellationToken);

    public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
        => runnerClient.ComposeDownAsync(deployment, cancellationToken);

    public Task<ComposeStatus> GetComposeStatusAsync(
        string projectName,
        Dictionary<string, string>? labels = null,
        CancellationToken cancellationToken = default)
        => runnerClient.GetComposeStatusAsync(projectName, labels, cancellationToken);
}
