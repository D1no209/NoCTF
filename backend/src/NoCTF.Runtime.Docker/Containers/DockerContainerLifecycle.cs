using System.Buffers;
using System.Globalization;
using System.Net;
using System.Diagnostics;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Callbacks;
using NoCTF.Application.Observability;
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
    private readonly TimeProvider timeProvider;

    public DockerContainerLifecycle(
        DockerRuntimeOptions options,
        TimeProvider? clock = null)
    {
        if (options.RuntimeLogMaxSizeBytes <= 0
            || options.RuntimeLogMaxFiles <= 0
            || options.OneShotOutputLimitBytesPerStream <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Docker runtime log and one-shot output limits must be positive.");
        this.options = options;
        timeProvider = clock ?? TimeProvider.System;
        client = new DockerClientBuilder()
            .WithEndpoint(new Uri(options.Endpoint))
            .Build();
    }

    public Task<ContainerReceipt> CreateAsync(
        ContainerRequest request,
        CancellationToken cancellationToken) =>
        CreateAsync(
            request,
            allowCompletedOneShot: false,
            startContainer: true,
            cancellationToken);

    private async Task<ContainerReceipt> CreateAsync(
        ContainerRequest request,
        bool allowCompletedOneShot,
        bool startContainer,
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
            var image = await EnsureImageAvailableAsync(request.Image, cancellationToken);
            ValidateRunAsNonRoot(request.Security, image.Config?.User);
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
            if (SupportsWsrx(request.AccessMode))
            {
                if (string.IsNullOrWhiteSpace(request.NetworkName))
                    throw new InvalidOperationException(
                        "A Docker WSRX Runtime requires an owned Runtime network.");
                await ConnectRuntimeProxyGatewaysAsync(
                    request.NetworkName,
                    cancellationToken);
            }
            if (request.AllowInternalCallback)
                await ConnectInternalCallbackAsync(request, response.ID, cancellationToken);
            if (startContainer)
            {
                await client.Containers.StartContainerAsync(
                    response.ID, new ContainerStartParameters(), cancellationToken);
            }
            var created = await client.Containers.InspectContainerAsync(
                response.ID, cancellationToken);
            var status = ToRuntimeStatus(created.State?.Status);
            if (startContainer && status == RuntimeStatus.Starting)
            {
                var deadline = timeProvider.GetUtcNow().Add(
                    request.OperationTimeout ?? TimeSpan.FromMinutes(2));
                while (status == RuntimeStatus.Starting && timeProvider.GetUtcNow() < deadline)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);
                    created = await client.Containers.InspectContainerAsync(
                        response.ID, cancellationToken);
                    status = ToRuntimeStatus(created.State?.Status);
                }
            }
            var accepted = startContainer
                ? status == RuntimeStatus.Running
                    || allowCompletedOneShot && status == RuntimeStatus.Stopped
                : status == RuntimeStatus.Pending;
            if (!accepted)
            {
                throw new InvalidOperationException(
                    $"Docker container {response.ID} did not reach the running state; current state is {status}.");
            }
            return new(request.OperationId, RuntimeProvider.Docker, response.ID, status,
                ReadPublishedPorts(created, request.PortMappings.Keys),
                options.PublicHost,
                SupportsWsrx(request.AccessMode)
                    ? ResolveInternalAddress(created, request.NetworkName)
                    : request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
                        ? "target"
                        : containerName,
                RuntimeInstanceId: request.RuntimeInstanceId);
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
                            request.RuntimeInstanceId ?? request.OperationId),
                        cleanupSource.Token);
                }
                catch
                {
                    // The failed create has no durable receipt; surface the cleanup failure.
                }
            }
            if (SupportsWsrx(request.AccessMode)
                && !string.IsNullOrWhiteSpace(request.NetworkName))
            {
                try
                {
                    await DisconnectRuntimeProxyGatewaysAsync(
                        request.NetworkName,
                        cleanupSource.Token);
                }
                catch
                {
                    // The failed provision has no durable receipt; the outer cleanup owns the network.
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
            var deadline = timeProvider.GetUtcNow().Add(timeout);
            while (status == RuntimeStatus.Starting && timeProvider.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);
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
                resourceName,
                NetworkId: SupportsWsrx(request.AccessMode)
                    ? request.NetworkName
                    : null), cancellationToken);
            return await CreateAsync(request, cancellationToken);
        }

        var internalHost = request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
            ? "target"
            : resourceName;
        if (SupportsWsrx(request.AccessMode))
        {
            if (string.IsNullOrWhiteSpace(request.NetworkName))
                throw new InvalidOperationException(
                    "A Docker WSRX Runtime requires an owned Runtime network.");
            await ConnectRuntimeProxyGatewaysAsync(
                request.NetworkName,
                cancellationToken);
            internalHost = ResolveInternalAddress(existing, request.NetworkName);
        }

        return new ContainerReceipt(
            request.OperationId,
            RuntimeProvider.Docker,
            existing.ID,
            RuntimeStatus.Running,
            ReadPublishedPorts(existing, request.PortMappings.Keys),
            options.PublicHost,
            internalHost,
            RuntimeInstanceId: request.RuntimeInstanceId);
    }

    private async Task<ImageInspectResponse> EnsureImageAvailableAsync(
        string image,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            // Pull below. Existing local images remain untouched.
        }

        var progress = new ImagePullProgress();
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image },
            DockerRegistryAuthentication.Read(image, options.RegistryConfigDirectory),
            progress,
            cancellationToken);
        if (progress.Error is not null)
        {
            throw new InvalidOperationException(
                $"Docker image pull failed: {progress.Error.Message}");
        }
        return await client.Images.InspectImageAsync(image, cancellationToken);
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        await DestroyAsync(
            receipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public async Task DestroyAsync(
        ContainerReceipt receipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        var warnings = new List<Exception>();
        var gracefulSucceeded = false;
        if (mode == RuntimeTerminationMode.GracefulThenForce)
        {
            var started = Stopwatch.GetTimestamp();
            using var graceful = CreateStageToken(cancellationToken, policy.GracefulStopTimeout);
            try
            {
                await client.Containers.StopContainerAsync(
                    receipt.ResourceId,
                    new ContainerStopParameters
                    {
                        WaitBeforeKillSeconds = checked((uint)Math.Max(
                            1,
                            Math.Ceiling(policy.GracefulStopTimeout.TotalSeconds)))
                    },
                    graceful.Token);
                gracefulSucceeded = true;
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "docker", "container", "graceful", "success",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (DockerContainerNotFoundException)
            {
                gracefulSucceeded = true;
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "docker", "container", "graceful", "absent",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "docker", "container", "graceful", "timeout",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                warnings.Add(exception);
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "docker", "container", "graceful", "warning",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }

        var forceStarted = Stopwatch.GetTimestamp();
        if (mode == RuntimeTerminationMode.Force || !gracefulSucceeded)
            NoCtfTelemetry.RecordRuntimeStopForce(
                "docker",
                mode == RuntimeTerminationMode.Force ? "requested" : "graceful_failed");
        using var force = CreateStageToken(cancellationToken, policy.ForceDeleteTimeout);
        try
        {
            await client.Containers.RemoveContainerAsync(
                receipt.ResourceId,
                new ContainerRemoveParameters { Force = true },
                force.Token);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "force_delete", "success",
                Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);
        }
        catch (DockerContainerNotFoundException)
        {
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "force_delete", "absent",
                Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add(exception);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "force_delete", "warning",
                Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);
        }

        var networkStarted = Stopwatch.GetTimestamp();
        using var network = CreateStageToken(cancellationToken, policy.NetworkCleanupTimeout);
        try
        {
            if (!string.IsNullOrWhiteSpace(receipt.NetworkId))
            {
                await DisconnectRuntimeProxyGatewaysAsync(
                    receipt.NetworkId,
                    network.Token);
            }
            await DeleteCallbackNetworkAsync(
                receipt.OperationId,
                new RuntimeResourceIdentity(
                    receipt.RuntimeInstanceId ?? receipt.OperationId),
                network.Token);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "network_cleanup", "success",
                Stopwatch.GetElapsedTime(networkStarted).TotalSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add(exception);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "network_cleanup", "warning",
                Stopwatch.GetElapsedTime(networkStarted).TotalSeconds);
        }

        var verificationStarted = Stopwatch.GetTimestamp();
        using var verification = CreateStageToken(cancellationToken, policy.VerificationTimeout);
        try
        {
            await WaitUntilDestroyedAsync(receipt, verification.Token);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "verification", "success",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            NoCtfTelemetry.RecordRuntimeStopResourcesRemaining("docker", "container");
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "container", "verification", "timeout",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
            throw new InvalidOperationException(
                "Docker Runtime resources remain after the termination budget.",
                warnings.Count == 0 ? null : new AggregateException(warnings));
        }
    }

    private async Task WaitUntilDestroyedAsync(
        ContainerReceipt receipt,
        CancellationToken cancellationToken)
    {
        var delays = new[] { 200, 400, 800, 1_000 };
        var attempt = 0;
        while (true)
        {
            var container = await GetAsync(
                RuntimeProvider.Docker,
                receipt.ResourceId,
                cancellationToken);
            var callbackNetwork = await FindNetworkAsync(
                CallbackNetworkName(receipt.OperationId),
                cancellationToken);
            var callbackNetworkRemains = callbackNetwork is not null
                && HasResourceIdentity(
                    callbackNetwork.Labels,
                    new RuntimeResourceIdentity(
                        receipt.RuntimeInstanceId ?? receipt.OperationId));
            if (container is null && !callbackNetworkRemains)
                return;
            await Task.Delay(
                TimeSpan.FromMilliseconds(delays[Math.Min(attempt++, delays.Length - 1)]),
                timeProvider,
                cancellationToken);
        }
    }

    private static CancellationTokenSource CreateStageToken(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
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

    public async Task<OneShotResult> RunAsync(
        ContainerRequest request,
        OneShotInputArchive? input,
        CancellationToken cancellationToken)
    {
        input?.Validate();
        var started = timeProvider.GetUtcNow();
        var receipt = await CreateAsync(
            request,
            allowCompletedOneShot: true,
            startContainer: input is null,
            cancellationToken);
        try
        {
            if (input is not null)
            {
                using var inputActivity = NoCtfTelemetry.ActivitySource.StartActivity(
                    "awdp.checker.input.prepare");
                inputActivity?.SetTag("runtime.provider", RuntimeProvider.Docker.ToString());
                inputActivity?.SetTag("awdp.checker.fix_input", true);
                try
                {
                    await client.Containers.ExtractArchiveToContainerAsync(
                        receipt.ResourceId,
                        new CopyToContainerParameters { Path = input.DestinationPath },
                        new CallerOwnedReadStream(input.Archive),
                        cancellationToken);
                    input.MarkPreparationCompleted();
                    inputActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    inputActivity?.SetTag(
                        "error.type",
                        "CheckerInputInjectionFailed");
                    inputActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
                    throw new OneShotInputPreparationException(
                        "Docker could not prepare the one-shot input archive.",
                        exception);
                }

                var containerStarted = await client.Containers.StartContainerAsync(
                    receipt.ResourceId,
                    new ContainerStartParameters(),
                    cancellationToken);
                if (!containerStarted)
                {
                    throw new InvalidOperationException(
                        "Docker did not start the prepared one-shot container.");
                }
            }

            var wait = await client.Containers.WaitContainerAsync(receipt.ResourceId, cancellationToken);
            using var logs = await client.Containers.GetContainerLogsAsync(receipt.ResourceId, new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true
            }, cancellationToken);
            var output = await ReadBoundedOutputAsync(logs, cancellationToken);
            return new(receipt.ResourceId, (int)wait.StatusCode, output.Stdout, output.Stderr,
                started, timeProvider.GetUtcNow());
        }
        finally
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            try
            {
                await DestroyAsync(receipt, cleanupSource.Token);
            }
            catch (Exception exception)
            {
                throw new OneShotCleanupException(
                    "Docker could not clean up the one-shot container.",
                    exception);
            }
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
        if (identity.RuntimeInstanceId == Guid.Empty)
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
            : $"noctf-rt-{identity.RuntimeInstanceId:N}";
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
                    return ambiguous.ID;
            }
            catch
            {
                // Preserve the original create failure when the ambiguous result
                // cannot be inspected safely.
            }
            throw;
        }
    }

    public async Task DeleteIsolatedNetworkAsync(
        string networkId,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Networks.DeleteNetworkAsync(networkId, cancellationToken);
        }
        catch (DockerApiException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Exact cleanup is idempotent. Duplicate delivery or reconciliation may
            // already have removed the isolated network.
        }
    }

    public async Task<bool> IsolatedNetworkExistsAsync(
        string networkId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.Networks.InspectNetworkAsync(networkId, cancellationToken);
            return true;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

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
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, timeoutSource.Token);
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
                        ?? request.OperationId).ToString("D")
                }
            }, cancellationToken);
            network = await client.Networks.InspectNetworkAsync(networkName, cancellationToken);
        }
        if (!network.Internal
            || network.Labels is null
            || !network.Labels.TryGetValue("noctf.io/network-purpose", out var purpose)
            || !string.Equals(purpose, CallbackNetworkPurpose(request), StringComparison.Ordinal)
            || !HasResourceIdentity(network.Labels, new RuntimeResourceIdentity(
                request.RuntimeInstanceId ?? request.OperationId)))
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
        if (request.AccessMode == RuntimeAccessMode.WsrxOnly
            && request.PortMappings.Count > 0)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "WSRX-only Docker runtimes cannot publish host ports.");
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
            request.RuntimeInstanceId ?? request.OperationId);
        var expectedJobKind = BuildLabels(request).GetValueOrDefault("noctf.io/job-kind") ?? JobKind(request.NetworkPurpose);
        if (!HasResourceIdentity(container.Config?.Labels, identity)
            || !HasJobKind(container.Config?.Labels, expectedJobKind)
            || !HasPublishedPortBindings(container.NetworkSettings?.Ports, request.PortMappings))
            throw new InvalidOperationException(
                $"Docker Container '{resourceName}' has a different ownership identity, purpose, or published port contract.");
        ValidateRunAsNonRoot(request.Security, container.Config?.User);
    }

    private static void ValidateRunAsNonRoot(
        ContainerSecurityPolicy security,
        string? imageUser)
    {
        if (!security.RunAsNonRoot)
            return;

        var parts = imageUser?.Split(':', StringSplitOptions.None);
        if (parts is not { Length: 1 or 2 }
            || !uint.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var uid)
            || uid == 0
            || parts.Length == 2
            && !uint.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new RuntimeConfigurationException(
                "Docker RunAsNonRoot requires the image Config.User to be a numeric non-zero UID or UID:GID.");
        }
    }

    private static bool HasPublishedPortBindings(
        IDictionary<string, IList<PortBinding>>? actual,
        IReadOnlyDictionary<int, int> expected)
    {
        if (expected.Count == 0)
        {
            return actual is null
                || actual.Count == 0
                || actual.Values.All(bindings => bindings is null
                    || bindings.Count == 0);
        }
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
                RuntimeInstanceId = target.Identity.RuntimeInstanceId,
                NetworkPurpose = ContainerNetworkPurpose.AwdChecker
            },
            null,
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
        && runtimeId == identity.RuntimeInstanceId;

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
                request.Identity.RuntimeInstanceId.ToString("D")
        };
        return labels;
    }

    private async Task ConnectRuntimeProxyGatewaysAsync(
        string networkId,
        CancellationToken cancellationToken)
    {
        var network = await client.Networks.InspectNetworkAsync(
            networkId,
            cancellationToken);
        var gateways = await ResolveRuntimeProxyContainersAsync(
            cancellationToken,
            requireAny: false);
        foreach (var gateway in gateways)
        {
            if (network.Containers?.ContainsKey(gateway) == true)
                continue;
            await client.Networks.ConnectNetworkAsync(
                network.ID,
                new NetworkConnectParameters { Container = gateway },
                cancellationToken);
        }
    }

    private async Task DisconnectRuntimeProxyGatewaysAsync(
        string networkId,
        CancellationToken cancellationToken)
    {
        NetworkResponse network;
        try
        {
            network = await client.Networks.InspectNetworkAsync(
                networkId,
                cancellationToken);
        }
        catch (DockerApiException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        var gateways = await ResolveRuntimeProxyContainersAsync(cancellationToken);
        foreach (var gateway in gateways)
        {
            if (network.Containers?.ContainsKey(gateway) != true)
                continue;
            await client.Networks.DisconnectNetworkAsync(
                network.ID,
                new NetworkDisconnectParameters
                {
                    Container = gateway,
                    Force = true
                },
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRuntimeProxyContainersAsync(
        CancellationToken cancellationToken,
        bool requireAny = true)
    {
        if (!string.IsNullOrWhiteSpace(options.ProxyContainerName))
        {
            ContainerInspectResponse configured;
            try
            {
                configured = await client.Containers.InspectContainerAsync(
                    options.ProxyContainerName,
                    cancellationToken);
            }
            catch (DockerContainerNotFoundException) when (!requireAny)
            {
                return [];
            }
            EnsureRuntimeProxyRole(configured.ID, configured.Config?.Labels);
            return [configured.ID];
        }

        var candidates = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = !requireAny,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{options.ProxyContainerLabelKey}={options.ProxyContainerLabelValue}"] = true
                    }
                }
            },
            cancellationToken);
        if (requireAny && candidates.Count == 0)
            throw new InvalidOperationException(
                "No running Runtime proxy gateway carries the required role label.");
        foreach (var candidate in candidates)
            EnsureRuntimeProxyRole(candidate.ID, candidate.Labels);
        return candidates.Select(candidate => candidate.ID)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private void EnsureRuntimeProxyRole(
        string containerId,
        IDictionary<string, string>? labels)
    {
        if (string.IsNullOrWhiteSpace(containerId)
            || labels is null
            || !labels.TryGetValue(options.ProxyContainerLabelKey, out var role)
            || !string.Equals(
                role,
                options.ProxyContainerLabelValue,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The configured Runtime proxy gateway does not carry the required role label.");
        }
    }

    private static string ResolveInternalAddress(
        ContainerInspectResponse container,
        string? requestedNetwork)
    {
        var networks = container.NetworkSettings?.Networks
            ?? throw new InvalidOperationException(
                "Docker Runtime has no network attachment metadata.");
        var endpoint = networks.FirstOrDefault(pair =>
                string.Equals(pair.Key, requestedNetwork, StringComparison.Ordinal)
                || string.Equals(
                    pair.Value.NetworkID,
                    requestedNetwork,
                    StringComparison.Ordinal))
            .Value;
        if (string.IsNullOrWhiteSpace(endpoint?.IPAddress))
            throw new InvalidOperationException(
                "Docker Runtime has no internal proxy address on its owned network.");
        return endpoint.IPAddress;
    }

    private static bool SupportsWsrx(RuntimeAccessMode mode) =>
        mode is RuntimeAccessMode.DirectAndWsrx or RuntimeAccessMode.WsrxOnly;

    private sealed class CallerOwnedReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);
        public override void Flush()
        {
        }
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            // Docker.DotNet disposes the stream it is given. The archive remains caller-owned.
        }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
