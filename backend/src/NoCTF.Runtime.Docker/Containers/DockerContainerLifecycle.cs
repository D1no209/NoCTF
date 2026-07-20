using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Docker.Containers;

/// <summary>Runs single-container challenge instances through Docker's native client.</summary>
public sealed class DockerContainerLifecycle : IContainerLifecycle, IOneShotJobRunner, IDisposable
{
    private readonly DockerClient client;
    private readonly DockerRuntimeOptions options;

    public DockerContainerLifecycle(DockerRuntimeOptions options)
    {
        this.options = options;
        client = new DockerClientConfiguration(new Uri(options.Endpoint)).CreateClient();
    }

    public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Docker)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider, "Docker runtime cannot create another provider.");

        var exposedPorts = request.PortMappings.Keys.ToDictionary(port => $"{port}/tcp", _ => new EmptyStruct());
        var bindings = request.PortMappings.ToDictionary(
            pair => $"{pair.Key}/tcp",
            pair => (IList<PortBinding>)[new() { HostPort = pair.Value.ToString() }]);
        var response = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = request.Image,
            Cmd = request.Command.ToList(),
            Env = request.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToList(),
            Labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value),
            ExposedPorts = exposedPorts,
            HostConfig = new HostConfig
            {
                PortBindings = bindings,
                NetworkMode = options.NetworkName,
                Memory = request.Limits.MemoryBytes,
                NanoCPUs = request.Limits.NanoCpus,
                PidsLimit = request.Limits.PidsLimit,
                SecurityOpt = request.Security.NoNewPrivileges ? ["no-new-privileges:true"] : [],
                ReadonlyRootfs = request.Security.ReadonlyRootfs,
                CapDrop = request.Security.CapDrop.ToList(),
                CapAdd = request.Security.CapAdd.ToList()
            }
        }, cancellationToken);
        await client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), cancellationToken);
        return new(request.OperationId, RuntimeProvider.Docker, response.ID, RuntimeStatus.Running, request.PortMappings, options.PublicHost, null);
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        await client.Containers.StopContainerAsync(receipt.ResourceId, new ContainerStopParameters(), cancellationToken);
        await client.Containers.RemoveContainerAsync(receipt.ResourceId, new ContainerRemoveParameters { Force = true }, cancellationToken);
    }

    public async Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken)
    {
        if (provider != RuntimeProvider.Docker)
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Docker runtime cannot query another provider.");
        try
        {
            var container = await client.Containers.InspectContainerAsync(resourceId, cancellationToken);
            return new(Guid.Empty, RuntimeProvider.Docker, resourceId, ToRuntimeStatus(container.State?.Status), new Dictionary<int, int>(),
                options.PublicHost, null);
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
    }

    public async Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var receipt = await CreateAsync(request, cancellationToken);
        var wait = await client.Containers.WaitContainerAsync(receipt.ResourceId, cancellationToken);
        using var logs = await client.Containers.GetContainerLogsAsync(receipt.ResourceId, true, new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true
        }, cancellationToken);
        await DestroyAsync(receipt, cancellationToken);
        var output = await logs.ReadOutputToEndAsync(cancellationToken);
        return new(receipt.ResourceId, (int)wait.StatusCode, output.stdout, output.stderr,
            started, DateTimeOffset.UtcNow);
    }

    public void Dispose() => client.Dispose();

    private static RuntimeStatus ToRuntimeStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "created" => RuntimeStatus.Pending,
        "restarting" => RuntimeStatus.Starting,
        "running" => RuntimeStatus.Running,
        "paused" or "removing" => RuntimeStatus.Stopping,
        "exited" or "dead" => RuntimeStatus.Stopped,
        _ => RuntimeStatus.Failed
    };
}
