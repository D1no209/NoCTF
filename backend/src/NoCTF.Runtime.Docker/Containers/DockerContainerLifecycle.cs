using System.Buffers;
using System.Globalization;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Callbacks;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Docker.Containers;

/// <summary>Runs single-container challenge instances through Docker's native client.</summary>
public sealed class DockerContainerLifecycle : IContainerLifecycle, IOneShotJobRunner,
    IAttachedOneShotJobRunner, IContainerSandboxLifecycle, IDisposable
{
    private const string NetworkPurposeAwdpCallback = "awdp-callback";
    private const string NetworkPurposeAwdCheckerCallback = "awd-checker-callback";
    private const string NetworkPurposeAwdChecker = "awd-checker";
    private const string NetworkPurposeAwdpVerification = "awdp-verification";
    private const string NetworkPurposePersistentRuntime = "persistent-runtime";
    private readonly DockerClient client;
    private readonly DockerRuntimeOptions options;

    public DockerContainerLifecycle(DockerRuntimeOptions options)
    {
        if (options.RuntimeLogMaxSizeBytes <= 0
            || options.RuntimeLogMaxFiles <= 0
            || options.OneShotOutputLimitBytesPerStream <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Docker runtime log and one-shot output limits must be positive.");
        this.options = options;
        client = new DockerClientBuilder()
            .WithEndpoint(new Uri(options.Endpoint))
            .Build();
    }

    public Task<ContainerReceipt> CreateAsync(
        ContainerRequest request,
        CancellationToken cancellationToken) =>
        CreateAsync(request, allowCompletedOneShot: false, cancellationToken);

    private async Task<ContainerReceipt> CreateAsync(
        ContainerRequest request,
        bool allowCompletedOneShot,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Docker)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider, "Docker runtime cannot create another provider.");
        ValidatePortMappings(request);

        var exposedPorts = request.ContainerPorts.ToDictionary(port => $"{port}/tcp", _ => new EmptyStruct());
        var bindings = request.PortMappings.ToDictionary(
            pair => $"{pair.Key}/tcp",
            pair => (IList<PortBinding>)[new()
            {
                HostPort = pair.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            }]);
        var containerName = $"noctf-{request.OperationId:N}";
        CreateContainerResponse? response = null;
        try
        {
            await EnsureImageAvailableAsync(request.Image, cancellationToken);
            var labels = BuildLabels(request);
            response = await client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = containerName,
                Image = request.Image,
                Cmd = request.Command.ToList(),
                Env = request.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToList(),
                Labels = labels,
                ExposedPorts = exposedPorts,
                NetworkingConfig = request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
                    && !string.IsNullOrWhiteSpace(request.NetworkName)
                    ? new NetworkingConfig
                    {
                        EndpointsConfig = new Dictionary<string, EndpointSettings>
                        {
                            [request.NetworkName] = new() { Aliases = ["target"] }
                        }
                    }
                    : null,
                HostConfig = new HostConfig
                {
                    PortBindings = bindings,
                    NetworkMode = request.NetworkName ?? options.NetworkName,
                    Memory = request.Limits.MemoryBytes,
                    NanoCPUs = request.Limits.NanoCpus,
                    PidsLimit = request.Limits.PidsLimit,
                    LogConfig = new LogConfig
                    {
                        Type = "local",
                        Config = new Dictionary<string, string>
                        {
                            ["max-size"] = options.RuntimeLogMaxSizeBytes.ToString(
                                CultureInfo.InvariantCulture),
                            ["max-file"] = options.RuntimeLogMaxFiles.ToString(
                                CultureInfo.InvariantCulture)
                        }
                    },
                    SecurityOpt = request.Security.NoNewPrivileges ? ["no-new-privileges:true"] : [],
                    ReadonlyRootfs = request.Security.ReadonlyRootfs,
                    CapDrop = request.Security.CapDrop.ToList(),
                    CapAdd = request.Security.CapAdd?.ToList() ?? []
                }
            }, cancellationToken);
            if (request.AllowInternalCallback)
                await ConnectInternalCallbackAsync(request, response.ID, cancellationToken);
            await client.Containers.StartContainerAsync(
                response.ID, new ContainerStartParameters(), cancellationToken);
            var created = await client.Containers.InspectContainerAsync(
                response.ID, cancellationToken);
            var status = ToRuntimeStatus(created.State?.Status);
            if (status == RuntimeStatus.Starting)
            {
                var deadline = DateTimeOffset.UtcNow.Add(
                    request.OperationTimeout ?? TimeSpan.FromMinutes(2));
                while (status == RuntimeStatus.Starting && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    created = await client.Containers.InspectContainerAsync(
                        response.ID, cancellationToken);
                    status = ToRuntimeStatus(created.State?.Status);
                }
            }
            if (status != RuntimeStatus.Running
                && !(allowCompletedOneShot && status == RuntimeStatus.Stopped))
            {
                throw new InvalidOperationException(
                    $"Docker container {response.ID} did not reach the running state; current state is {status}.");
            }
            return new(request.OperationId, RuntimeProvider.Docker, response.ID, status,
                ReadPublishedPorts(created, request.PortMappings.Keys),
                options.PublicHost,
                request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
                    ? "target"
                    : containerName,
                RuntimeInstanceId: request.RuntimeInstanceId,
                Generation: request.Generation);
        }
        catch
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            try
            {
                await RemoveContainerAsync(response?.ID ?? containerName, cleanupSource.Token);
            }
            catch
            {
                // The failed create has no durable receipt; surface the cleanup failure.
            }
            if (request.AllowInternalCallback)
            {
                try
                {
                    await DeleteCallbackNetworkAsync(
                        request.OperationId,
                        new RuntimeResourceIdentity(
                            request.RuntimeInstanceId ?? request.OperationId,
                            request.Generation),
                        cleanupSource.Token);
                }
                catch
                {
                    // The failed create has no durable receipt; surface the cleanup failure.
                }
            }
            throw;
        }
    }

    public async Task<ContainerReceipt> EnsureRunningAsync(
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Docker)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider,
                "Docker runtime cannot reconcile another provider.");
        ValidatePortMappings(request);

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

        ValidateExistingContainer(existing, resourceName, request);

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
            ReadPublishedPorts(existing, request.PortMappings.Keys),
            options.PublicHost,
            resourceName,
            RuntimeInstanceId: request.RuntimeInstanceId,
            Generation: request.Generation);
    }

    private async Task EnsureImageAvailableAsync(
        string image,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.Images.InspectImageAsync(image, cancellationToken);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            // Pull below. Existing local images remain untouched.
        }

        var progress = new ImagePullProgress();
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image },
            new AuthConfig(),
            progress,
            cancellationToken);
        if (progress.Error is not null)
        {
            throw new InvalidOperationException(
                $"Docker image pull failed: {progress.Error.Message}");
        }
        _ = await client.Images.InspectImageAsync(image, cancellationToken);
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await client.Containers.StopContainerAsync(receipt.ResourceId, new ContainerStopParameters(), cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Destroy is idempotent: an externally removed runtime is already stopped.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        try
        {
            await client.Containers.RemoveContainerAsync(
                receipt.ResourceId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // A failed or skipped stop may still race with external cleanup.
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        try
        {
            if (receipt.Generation > 0)
                await DeleteCallbackNetworkAsync(
                    receipt.OperationId,
                    new RuntimeResourceIdentity(
                        receipt.RuntimeInstanceId ?? receipt.OperationId,
                        receipt.Generation),
                    cancellationToken);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
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
        var receipt = await CreateAsync(request, allowCompletedOneShot: true, cancellationToken);
        try
        {
            var wait = await client.Containers.WaitContainerAsync(receipt.ResourceId, cancellationToken);
            using var logs = await client.Containers.GetContainerLogsAsync(receipt.ResourceId, new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true
            }, cancellationToken);
            var output = await ReadBoundedOutputAsync(logs, cancellationToken);
            return new(receipt.ResourceId, (int)wait.StatusCode, output.Stdout, output.Stderr,
                started, DateTimeOffset.UtcNow);
        }
        finally
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            await DestroyAsync(receipt, cleanupSource.Token);
        }
    }

    private async Task<(string Stdout, string Stderr)> ReadBoundedOutputAsync(
        MultiplexedStream logs,
        CancellationToken cancellationToken)
    {
        var limit = options.OneShotOutputLimitBytesPerStream;
        using var stdout = new MemoryStream(Math.Min(limit, 81_920));
        using var stderr = new MemoryStream(Math.Min(limit, 81_920));
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            while (true)
            {
                var read = await logs.ReadOutputAsync(
                    buffer,
                    0,
                    buffer.Length,
                    cancellationToken);
                if (read.Count > 0)
                {
                    if (read.Target == MultiplexedStream.TargetStream.StandardOut)
                        AppendBounded(stdout, buffer, read.Count, limit);
                    else if (read.Target == MultiplexedStream.TargetStream.StandardError)
                        AppendBounded(stderr, buffer, read.Count, limit);
                }
                if (read.EOF)
                    break;
            }

            return (
                Encoding.UTF8.GetString(
                    stdout.GetBuffer(),
                    0,
                    checked((int)stdout.Length)),
                Encoding.UTF8.GetString(
                    stderr.GetBuffer(),
                    0,
                    checked((int)stderr.Length)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendBounded(
        MemoryStream destination,
        byte[] buffer,
        int count,
        int limit)
    {
        var remaining = limit - checked((int)destination.Length);
        if (remaining > 0)
            destination.Write(buffer, 0, Math.Min(remaining, count));
    }

    public Task CopyArchiveAsync(ContainerReceipt receipt, Stream tarArchive, CancellationToken cancellationToken) =>
        client.Containers.ExtractArchiveToContainerAsync(receipt.ResourceId,
            new CopyToContainerParameters { Path = "/" }, tarArchive, cancellationToken);

    public async Task<string> CreateIsolatedNetworkAsync(
        ContainerNetworkPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var identity = request.Identity;
        if (identity.RuntimeInstanceId == Guid.Empty
            || identity.Generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!Enum.IsDefined(request.Purpose)
            || !Enum.IsDefined(request.EgressPolicy)
            || request.PublicIngressPorts.Any(port => port is < 1 or > 65535))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Purpose == ContainerNetworkPurpose.AwdpVerification
            && request.TargetPort is not (>= 1 and <= 65535))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.EgressPolicy != RuntimeEgressPolicy.Isolated)
            throw new InvalidOperationException(
                "Docker container runtimes do not support InternetOnly egress.");
        var name = request.Purpose == ContainerNetworkPurpose.AwdpVerification
            ? $"noctf-awdp-{identity.RuntimeInstanceId:N}"
            : $"noctf-rt-{identity.RuntimeInstanceId:N}-{identity.Generation}";
        var purpose = request.Purpose == ContainerNetworkPurpose.AwdpVerification
            ? "awdp-verification"
            : "persistent-runtime";
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
        {
            if (current.Internal
                || !HasResourceIdentity(current.Labels, identity)
                || !HasNetworkPurpose(current.Labels, purpose))
                throw new InvalidOperationException(
                    "The existing runtime network has a different ownership identity or purpose.");
            return current.ID;
        }

        try
        {
            var response = await client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = name,
                Internal = false,
                Labels = NetworkLabels(request, purpose)
            }, cancellationToken);
            return response.ID;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            try
            {
                var ambiguous = await FindNetworkAsync(name, cleanup.Token);
                if (ambiguous is not null
                    && HasResourceIdentity(ambiguous.Labels, identity)
                    && HasNetworkPurpose(ambiguous.Labels, purpose))
                    await DeleteNetworkAsync(ambiguous, cleanup.Token);
            }
            catch
            {
                // The ambiguous network could not be safely cleaned up.
            }
            throw;
        }
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

    public void Dispose() => client.Dispose();

    private async Task<string> ResolveContainerTargetNetworkAsync(
        AttachedContainerRuntimeTarget target,
        CancellationToken cancellationToken)
    {
        var receipt = target.Receipt;
        if (receipt.Provider != RuntimeProvider.Docker
            || receipt.RuntimeInstanceId != target.Identity.RuntimeInstanceId
            || receipt.Generation != target.Identity.Generation
            || string.IsNullOrWhiteSpace(receipt.NetworkId))
            throw new InvalidOperationException(
                "The attached Container receipt has a different ownership identity.");
        var network = await client.Networks.InspectNetworkAsync(
            receipt.NetworkId,
            cancellationToken);
        if (!HasResourceIdentity(network.Labels, target.Identity)
            || !HasNetworkPurpose(network.Labels, "persistent-runtime"))
            throw new InvalidOperationException(
                "The attached Container network has a different ownership identity.");
        return network.ID;
    }

    private async Task<string> ResolveComposeTargetNetworkAsync(
        AttachedComposeRuntimeTarget target,
        CancellationToken cancellationToken)
    {
        var receipt = target.Receipt;
        if (receipt.Provider != RuntimeProvider.Docker
            || receipt.OperationId != target.Identity.RuntimeInstanceId
            || receipt.Generation != target.Identity.Generation
            || string.IsNullOrWhiteSpace(receipt.ProjectName)
            || string.IsNullOrWhiteSpace(target.ServiceName))
            throw new InvalidOperationException(
                "The attached Compose receipt has a different ownership identity.");
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"com.docker.compose.project={receipt.ProjectName}"] = true,
                        [$"com.docker.compose.service={target.ServiceName}"] = true
                    }
                }
            },
            cancellationToken);
        var container = containers.SingleOrDefault(item =>
            HasResourceIdentity(item.Labels, target.Identity)
            && HasJobKind(item.Labels, "persistent-runtime"))
            ?? throw new InvalidOperationException(
                "The attached Compose service was not found with the required ownership identity.");
        var inspected = await client.Containers.InspectContainerAsync(
            container.ID,
            cancellationToken);
        var networkIds = inspected.NetworkSettings?.Networks?.Values
            .Select(endpoint => endpoint.NetworkID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var owned = new List<NetworkResponse>();
        foreach (var networkId in networkIds)
        {
            var network = await client.Networks.InspectNetworkAsync(
                networkId,
                cancellationToken);
            if (HasResourceIdentity(network.Labels, target.Identity)
                && HasJobKind(network.Labels, "persistent-runtime")
                && HasLabel(
                    network.Labels,
                    "com.docker.compose.project",
                    receipt.ProjectName))
                owned.Add(network);
        }
        return owned
            .OrderBy(network => network.Name, StringComparer.Ordinal)
            .Select(network => network.ID)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "The attached Compose service has no owned runtime network.");
    }

    private async Task ConnectInternalCallbackAsync(
        ContainerRequest request,
        string checkerContainerId,
        CancellationToken cancellationToken)
    {
        if (request.Generation <= 0)
            throw new InvalidOperationException("A scoring checker requires a positive Runtime generation.");
        Uri? callback = null;
        if (request.NetworkPurpose != ContainerNetworkPurpose.PersistentRuntime
            && (!request.Environment.TryGetValue("NOCTF_CALLBACK_URL", out var callbackText)
                || !Uri.TryCreate(callbackText, UriKind.Absolute, out callback)
                || callback.Scheme is not ("http" or "https")))
        {
            throw new InvalidOperationException(
                "A scoring checker callback requires an absolute HTTP(S) callback URL.");
        }
        var callbackContainers = await ResolveInternalCallbackContainersAsync(cancellationToken);

        var networkName = CallbackNetworkName(request.OperationId);
        var network = await FindNetworkAsync(networkName, cancellationToken);
        if (network is null)
        {
            _ = await client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = networkName,
                Internal = true,
                Labels = new Dictionary<string, string>
                {
                    ["noctf.io/network-purpose"] = CallbackNetworkPurpose(request),
                    ["noctf.io/managed"] = "true",
                    ["noctf.io/job-kind"] = JobKind(request.NetworkPurpose),
                    ["noctf.io/runtime-instance-id"] = (request.RuntimeInstanceId
                        ?? request.OperationId).ToString("D"),
                    ["noctf.io/generation"] = request.Generation.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                }
            }, cancellationToken);
            network = await client.Networks.InspectNetworkAsync(networkName, cancellationToken);
        }
        if (!network.Internal
            || network.Labels is null
            || !network.Labels.TryGetValue("noctf.io/network-purpose", out var purpose)
            || !string.Equals(purpose, CallbackNetworkPurpose(request), StringComparison.Ordinal)
            || !HasResourceIdentity(network.Labels, new RuntimeResourceIdentity(
                request.RuntimeInstanceId ?? request.OperationId,
                request.Generation)))
            throw new InvalidOperationException("The checker callback network is not an internal managed network.");

        foreach (var callbackContainerId in callbackContainers)
        {
            if (network.Containers?.ContainsKey(callbackContainerId) == true)
                continue;
            await client.Networks.ConnectNetworkAsync(network.ID, new NetworkConnectParameters
            {
                Container = callbackContainerId,
                EndpointConfig = new EndpointSettings
                {
                    Aliases = callback is null ? [] : [callback.Host]
                }
            }, cancellationToken);
        }
        await client.Networks.ConnectNetworkAsync(
            network.ID,
            new NetworkConnectParameters
            {
                Container = checkerContainerId,
                EndpointConfig = new EndpointSettings
                {
                    Aliases = request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
                        ? ["target"]
                        : []
                }
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ResolveInternalCallbackContainersAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.CallbackContainerName))
        {
            var configured = await client.Containers.InspectContainerAsync(
                options.CallbackContainerName,
                cancellationToken);
            EnsureInternalCallbackRole(configured.ID, configured.Config?.Labels);
            return [configured.ID];
        }

        var candidates = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{options.CallbackContainerLabelKey}={options.CallbackContainerLabelValue}"] =
                            true
                    }
                }
            },
            cancellationToken);
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "No running callback container carries the required role label.");
        foreach (var candidate in candidates)
            EnsureInternalCallbackRole(candidate.ID, candidate.Labels);
        return candidates
            .Select(candidate => candidate.ID)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private void EnsureInternalCallbackRole(
        string containerId,
        IDictionary<string, string>? labels)
    {
        if (string.IsNullOrWhiteSpace(containerId)
            || labels is null
            || !labels.TryGetValue(options.CallbackContainerLabelKey, out var callbackRole)
            || !string.Equals(
                callbackRole,
                options.CallbackContainerLabelValue,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The configured callback container does not carry the required role label.");
    }

    private async Task DeleteCallbackNetworkAsync(
        Guid operationId,
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        var network = await FindNetworkAsync(CallbackNetworkName(operationId), cancellationToken);
        if (network is null || !HasResourceIdentity(network.Labels, identity))
            return;
        await DeleteNetworkAsync(network, cancellationToken);
    }

    private async Task DeleteNetworkAsync(
        NetworkResponse network,
        CancellationToken cancellationToken)
    {
        if (network.Labels?.TryGetValue("noctf.io/network-purpose", out var purpose) == true
            && purpose is NetworkPurposeAwdpCallback or NetworkPurposeAwdCheckerCallback)
        {
            var current = await client.Networks.InspectNetworkAsync(network.ID, cancellationToken);
            foreach (var containerId in current.Containers?.Keys ?? [])
            {
                try
                {
                    await client.Networks.DisconnectNetworkAsync(
                        network.ID,
                        new NetworkDisconnectParameters { Container = containerId, Force = true },
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Force deletion below is still attempted for stale endpoints.
                }
            }
        }
        await client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
    }

    private async Task<NetworkResponse?> FindNetworkAsync(
        string networkName,
        CancellationToken cancellationToken)
    {
        var networks = await client.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [networkName] = true }
            }
        }, cancellationToken);
        return networks.SingleOrDefault(network =>
            string.Equals(network.Name, networkName, StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<int, int> ReadPublishedPorts(
        ContainerInspectResponse container,
        IEnumerable<int> requestedPorts)
    {
        var actual = container.NetworkSettings?.Ports
            ?? throw new InvalidOperationException("Docker did not report network port bindings.");
        var mappings = new Dictionary<int, int>();
        foreach (var containerPort in requestedPorts)
        {
            if (!actual.TryGetValue($"{containerPort}/tcp", out var bindings)
                || bindings is null
                || !TryGetPublishedHostPort(bindings, out var hostPort))
                throw new InvalidOperationException(
                    $"Docker did not assign a host port for container port {containerPort}.");
            mappings.Add(containerPort, hostPort);
        }
        return mappings;
    }

    private static void ValidatePortMappings(ContainerRequest request)
    {
        if (request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
            && request.PortMappings.Any(mapping => mapping.Value != 0))
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Persistent Docker runtimes must request Docker-assigned host ports.");
        if (request.NetworkPurpose != ContainerNetworkPurpose.PersistentRuntime
            && request.PortMappings.Any(mapping => mapping.Value is < 0 or > 65535))
            throw new ArgumentOutOfRangeException(nameof(request));
    }

    private static void ValidateExistingContainer(
        ContainerInspectResponse container,
        string resourceName,
        ContainerRequest request)
    {
        var identity = new RuntimeResourceIdentity(
            request.RuntimeInstanceId ?? request.OperationId,
            request.Generation);
        if (!HasResourceIdentity(container.Config?.Labels, identity)
            || !HasJobKind(container.Config?.Labels, JobKind(request.NetworkPurpose))
            || !HasPublishedPortBindings(container.NetworkSettings?.Ports, request.PortMappings))
            throw new InvalidOperationException(
                $"Docker Container '{resourceName}' has a different ownership identity, purpose, or published port contract.");
    }

    private static bool HasPublishedPortBindings(
        IDictionary<string, IList<PortBinding>>? actual,
        IReadOnlyDictionary<int, int> expected)
    {
        if (expected.Count == 0)
            return actual is null || actual.Count == 0;
        if (actual is null || actual.Count != expected.Count)
            return false;

        foreach (var mapping in expected)
        {
            if (!actual.TryGetValue($"{mapping.Key}/tcp", out var bindings)
                || !TryGetPublishedHostPort(bindings, out var hostPort)
                || mapping.Value != 0 && hostPort != mapping.Value)
                return false;
        }
        return true;
    }

    private static bool TryGetPublishedHostPort(
        IEnumerable<PortBinding> bindings,
        out int hostPort)
    {
        var ports = bindings
            .Select(binding => int.TryParse(
                binding.HostPort,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : 0)
            .Where(port => port is >= 1 and <= 65535)
            .Distinct()
            .ToArray();
        hostPort = ports.Length == 1 ? ports[0] : 0;
        return hostPort != 0;
    }

    private async Task RemoveContainerAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(
                resourceId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Deletion is idempotent.
        }
    }

    public async Task<OneShotResult> RunAttachedAsync(
        ContainerRequest request,
        AttachedRuntimeTarget target,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Docker)
            throw new InvalidOperationException(
                "An attached Docker job requires the Docker provider.");
        var networkId = target switch
        {
            AttachedContainerRuntimeTarget container =>
                await ResolveContainerTargetNetworkAsync(container, cancellationToken),
            AttachedComposeRuntimeTarget compose =>
                await ResolveComposeTargetNetworkAsync(compose, cancellationToken),
            _ => throw new InvalidOperationException("The attached Runtime target is unsupported.")
        };
        return await RunAsync(
            request with
            {
                NetworkName = networkId,
                AllowInternalCallback = true,
                Generation = target.Identity.Generation,
                RuntimeInstanceId = target.Identity.RuntimeInstanceId,
                NetworkPurpose = ContainerNetworkPurpose.AwdChecker
            },
            cancellationToken);
    }


    private static Dictionary<string, string> BuildLabels(ContainerRequest request)
    {
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (request.AllowInternalCallback)
        {
            labels["noctf.io/managed"] = "true";
            labels["noctf.io/job-kind"] = JobKind(request.NetworkPurpose);
            labels["noctf.io/runtime-instance-id"] = (request.RuntimeInstanceId
                ?? request.OperationId).ToString("D");
            labels["noctf.io/generation"] = request.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        return labels;
    }

    private static string CallbackNetworkName(Guid operationId) =>
        $"noctf-callback-{operationId:N}";

    private static string CallbackNetworkPurpose(ContainerRequest request) =>
        request.NetworkPurpose == ContainerNetworkPurpose.AwdChecker
            ? NetworkPurposeAwdCheckerCallback
            : NetworkPurposeAwdpCallback;

    private static string JobKind(ContainerNetworkPurpose purpose) => purpose switch
    {
        ContainerNetworkPurpose.AwdChecker => NetworkPurposeAwdChecker,
        ContainerNetworkPurpose.AwdpVerification => NetworkPurposeAwdpVerification,
        ContainerNetworkPurpose.PersistentRuntime => NetworkPurposePersistentRuntime,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    private static RuntimeStatus ToRuntimeStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "created" => RuntimeStatus.Pending,
        "restarting" => RuntimeStatus.Starting,
        "running" => RuntimeStatus.Running,
        "paused" or "removing" => RuntimeStatus.Stopping,
        "exited" or "dead" => RuntimeStatus.Stopped,
        _ => RuntimeStatus.Failed
    };

    private static bool HasResourceIdentity(
        IDictionary<string, string>? labels,
        RuntimeResourceIdentity identity) =>
        labels is not null
        && labels.TryGetValue("noctf.io/managed", out var managed)
        && string.Equals(managed, "true", StringComparison.Ordinal)
        && labels.TryGetValue("noctf.io/runtime-instance-id", out var runtimeText)
        && Guid.TryParse(runtimeText, out var runtimeId)
        && runtimeId == identity.RuntimeInstanceId
        && labels.TryGetValue("noctf.io/generation", out var generationText)
        && int.TryParse(generationText, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var generation)
        && generation == identity.Generation;

    private static bool HasJobKind(
        IDictionary<string, string>? labels,
        string jobKind) => HasLabel(labels, "noctf.io/job-kind", jobKind);

    private static bool HasLabel(
        IDictionary<string, string>? labels,
        string key,
        string value) =>
        labels is not null
        && labels.TryGetValue(key, out var actual)
        && string.Equals(actual, value, StringComparison.Ordinal);

    private static bool HasNetworkPurpose(
        IDictionary<string, string>? labels,
        string purpose) =>
        labels is not null
        && labels.TryGetValue("noctf.io/network-purpose", out var value)
        && string.Equals(value, purpose, StringComparison.Ordinal);

    private static Dictionary<string, string> NetworkLabels(
        ContainerNetworkPolicyRequest request,
        string purpose)
    {
        var labels = new Dictionary<string, string>
        {
            ["noctf.io/job-kind"] = purpose,
            ["noctf.io/network-purpose"] = purpose,
            ["noctf.io/managed"] = "true",
            ["noctf.io/runtime-instance-id"] =
                request.Identity.RuntimeInstanceId.ToString("D"),
            ["noctf.io/generation"] = request.Identity.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };
        return labels;
    }

    private sealed class ImagePullProgress : IProgress<JSONMessage>
    {
        public JSONError? Error { get; private set; }

        public void Report(JSONMessage value)
        {
            if (value.Error is not null)
                Error = value.Error;
        }
    }

}
