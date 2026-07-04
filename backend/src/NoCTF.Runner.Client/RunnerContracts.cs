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
        var response = await httpClient.PostAsJsonAsync("/runner/containers", config, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerInstance>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty container response.");
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/runner/containers/destroy", container, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/runner/jobs/one-shot", config, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContainerRunResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty job response.");
    }

    public async Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/runner/compose/up", config, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ComposeDeployment>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty compose response.");
    }

    public async Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/runner/compose/down", deployment, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ComposeStatus> GetComposeStatusAsync(string projectName, Dictionary<string, string>? labels = null, CancellationToken ct = default)
    {
        var query = labels is null || labels.Count == 0
            ? string.Empty
            : "?" + string.Join("&", labels.Select(kvp =>
                $"label={Uri.EscapeDataString($"{kvp.Key}={kvp.Value}")}"));
        var response = await httpClient.GetAsync($"/runner/compose/{Uri.EscapeDataString(projectName)}/status{query}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ComposeStatus>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Runner returned an empty compose status response.");
    }
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
