using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Docker.Containers;

/// <summary>Runs single-container challenge instances through Docker's native client.</summary>
public sealed class DockerContainerLifecycle : IContainerLifecycle, IOneShotJobRunner, IContainerSandboxLifecycle,
    IRuntimeResourceReaper, IDisposable
{
    private readonly DockerClient client;
    private readonly DockerRuntimeOptions options;

    public DockerContainerLifecycle(DockerRuntimeOptions options)
    {
        this.options = options;
        client = new DockerClientBuilder()
            .WithEndpoint(new Uri(options.Endpoint))
            .Build();
    }

    public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Docker)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider, "Docker runtime cannot create another provider.");

        var exposedPorts = request.ContainerPorts.ToDictionary(port => $"{port}/tcp", _ => new EmptyStruct());
        var bindings = request.PortMappings.ToDictionary(
            pair => $"{pair.Key}/tcp",
            pair => (IList<PortBinding>)[new() { HostPort = pair.Value.ToString() }]);
        var containerName = $"noctf-{request.OperationId:N}";
        var response = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = containerName,
            Image = request.Image,
            Cmd = request.Command.ToList(),
            Env = request.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToList(),
            Labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value),
            ExposedPorts = exposedPorts,
            HostConfig = new HostConfig
            {
                PortBindings = bindings,
                NetworkMode = request.NetworkName ?? options.NetworkName,
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
        return new(request.OperationId, RuntimeProvider.Docker, response.ID, RuntimeStatus.Running,
            request.PortMappings, options.PublicHost, containerName);
    }

    public async Task<ContainerReceipt> EnsureRunningAsync(
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Docker)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider,
                "Docker runtime cannot reconcile another provider.");

        var resourceName = $"noctf-{request.OperationId:N}";
        ContainerInspectResponse? existing;
        try
        {
            existing = await client.Containers.InspectContainerAsync(resourceName, cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            return await CreateAsync(request, cancellationToken);
        }

        var status = ToRuntimeStatus(existing.State?.Status);
        if (status == RuntimeStatus.Pending)
        {
            await client.Containers.StartContainerAsync(
                existing.ID,
                new ContainerStartParameters(),
                cancellationToken);
            existing = await client.Containers.InspectContainerAsync(existing.ID, cancellationToken);
            status = ToRuntimeStatus(existing.State?.Status);
        }

        if (status == RuntimeStatus.Starting)
        {
            var timeout = request.OperationTimeout ?? TimeSpan.FromMinutes(2);
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            while (status == RuntimeStatus.Starting && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                existing = await client.Containers.InspectContainerAsync(existing.ID, cancellationToken);
                status = ToRuntimeStatus(existing.State?.Status);
            }
        }

        if (status != RuntimeStatus.Running)
        {
            await DestroyAsync(new ContainerReceipt(
                request.OperationId,
                RuntimeProvider.Docker,
                existing.ID,
                status,
                request.PortMappings,
                options.PublicHost,
                resourceName), cancellationToken);
            return await CreateAsync(request, cancellationToken);
        }

        return new ContainerReceipt(
            request.OperationId,
            RuntimeProvider.Docker,
            existing.ID,
            RuntimeStatus.Running,
            request.PortMappings,
            options.PublicHost,
            resourceName);
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            await client.Containers.StopContainerAsync(receipt.ResourceId, new ContainerStopParameters(), cancellationToken);
            await client.Containers.RemoveContainerAsync(receipt.ResourceId, new ContainerRemoveParameters { Force = true }, cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Destroy is idempotent: an externally removed runtime is already stopped.
        }
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
        try
        {
            var wait = await client.Containers.WaitContainerAsync(receipt.ResourceId, cancellationToken);
            using var logs = await client.Containers.GetContainerLogsAsync(receipt.ResourceId, new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true
            }, cancellationToken);
            var output = await logs.ReadOutputToEndAsync(cancellationToken);
            return new(receipt.ResourceId, (int)wait.StatusCode, output.stdout, output.stderr,
                started, DateTimeOffset.UtcNow);
        }
        finally
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            await DestroyAsync(receipt, cleanupSource.Token);
        }
    }

    public Task CopyArchiveAsync(ContainerReceipt receipt, Stream tarArchive, CancellationToken cancellationToken) =>
        client.Containers.ExtractArchiveToContainerAsync(receipt.ResourceId,
            new CopyToContainerParameters { Path = "/" }, tarArchive, cancellationToken);

    public async Task<string> CreateIsolatedNetworkAsync(
        Guid operationId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var name = $"noctf-awdp-{operationId:N}";
        var existing = await client.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [name] = true }
            }
        }, cancellationToken);
        var current = existing.SingleOrDefault(network =>
            string.Equals(network.Name, name, StringComparison.Ordinal));
        if (current is not null)
            return current.ID;

        var response = await client.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
            Name = name,
            Internal = true,
            Labels = new Dictionary<string, string>
            {
                ["noctf.io/job-kind"] = "awdp-verification",
                ["noctf.io/expires-at"] = expiresAt.ToUnixTimeSeconds().ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            }
        }, cancellationToken);
        return response.ID;
    }

    public Task DeleteIsolatedNetworkAsync(string networkId, CancellationToken cancellationToken) =>
        client.Networks.DeleteNetworkAsync(networkId, cancellationToken);

    public async Task<ContainerExecResult> ExecAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await RunExecAsync(receipt, command, null, timeout, cancellationToken);

    public async Task<ContainerExecResult> ExecWithInputAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await RunExecAsync(receipt, command, standardInput, timeout, cancellationToken);

    private async Task<ContainerExecResult> RunExecAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        ReadOnlyMemory<byte>? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var created = await client.Exec.CreateContainerExecAsync(receipt.ResourceId, new ContainerExecCreateParameters
        {
            Cmd = command.ToList(),
            AttachStdin = standardInput is not null,
            AttachStdout = false,
            AttachStderr = false
        }, cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var stream = await client.Exec.StartContainerExecAsync(created.ID,
                new ContainerExecStartParameters { Detach = false, TTY = false }, timeoutSource.Token);
            if (standardInput is { } inputMemory)
            {
                var input = inputMemory.ToArray();
                await stream.WriteAsync(input, 0, input.Length, timeoutSource.Token);
                stream.CloseWrite();
            }
            while (true)
            {
                var state = await client.Exec.InspectContainerExecAsync(created.ID, timeoutSource.Token);
                if (!state.Running)
                    return new(state.ExitCode is { } exitCode ? checked((int)exitCode) : -1, false);
                await Task.Delay(100, timeoutSource.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            await StopContainerAfterExecTimeoutAsync(receipt.ResourceId);
            cancellationToken.ThrowIfCancellationRequested();
            return new(-1, true);
        }
    }

    private async Task StopContainerAfterExecTimeoutAsync(string resourceId)
    {
        using var killSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.Containers.KillContainerAsync(resourceId,
                new ContainerKillParameters { Signal = "SIGKILL" }, killSource.Token);
        }
        catch (DockerContainerNotFoundException)
        {
            // The owning cleanup path has already removed the container.
        }
    }

    public async Task<RuntimeResourceReapResult> ReapExpiredAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var removed = 0;
        var failed = 0;
        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["label"] = new Dictionary<string, bool> { ["noctf.io/expires-at"] = true }
        };
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Filters = filters }, cancellationToken);
        foreach (var container in containers.Where(item => IsExpired(item.Labels, now)))
        {
            try
            {
                await client.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true }, cancellationToken);
                removed++;
            }
            catch (DockerContainerNotFoundException)
            {
                removed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
            }
        }

        var networks = await client.Networks.ListNetworksAsync(
            new NetworksListParameters { Filters = filters }, cancellationToken);
        foreach (var network in networks.Where(item => IsExpired(item.Labels, now)))
        {
            try
            {
                await client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
                removed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
            }
        }
        return new(removed, failed);
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

    private static bool IsExpired(IDictionary<string, string>? labels, DateTimeOffset now) =>
        labels is not null
        && labels.TryGetValue("noctf.io/expires-at", out var text)
        && long.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var expiresAt)
        && expiresAt <= now.ToUnixTimeSeconds();
}
