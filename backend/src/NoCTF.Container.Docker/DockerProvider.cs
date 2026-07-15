using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.PluginBase;
using System.Net;

namespace NoCTF.Container.Docker;

public sealed class DockerProvider : IContainerProvider<IDockerClient, DockerContainerMetadata>, IDisposable
{
    internal const string ManagedLabel = "noctf.managed";
    internal const string OperationLabel = "noctf.operation-id";
    internal const string RequestFingerprintLabel = "noctf.request-fingerprint";
    internal const string RunReceiptLabel = "noctf.run-receipt";
    internal const string RunDeadlineLabel = "noctf.run-deadline";
    internal const string RuntimeExpiryLabel = "noctf.runtime-expiry";
    internal const string RuntimeContainerLabel = "noctf.runtime-container";
    internal const string OwnedNetworkLabel = "noctf.owned-network";
    internal const string ComposeResourceLabel = "noctf.compose-resource";
    internal const string ComposeProjectLabel = "noctf.compose-project";
    internal const string ComposeClaimLabel = "noctf.compose-claim";
    internal const string ComposeMutexLabel = "noctf.compose-mutex";
    internal const string ComposeMutexOwnerLabel = "noctf.compose-mutex-owner";
    internal const string ComposeMutexDeadlineLabel = "noctf.compose-mutex-deadline";

    private readonly DockerClientConfiguration? _configuration;
    private readonly IDockerClient _client;
    private readonly TimeProvider _timeProvider;

    public DockerProvider(
        string? dockerHost = null,
        string? publishedHost = null,
        TimeSpan? runReceiptRetention = null,
        TimeSpan? runtimeCleanupGrace = null)
    {
        _configuration = dockerHost is not null
            ? new DockerClientConfiguration(new Uri(dockerHost))
            : new DockerClientConfiguration();
        _client = _configuration.CreateClient();
        PublishedHost = string.IsNullOrWhiteSpace(publishedHost) ? "127.0.0.1" : publishedHost.Trim();
        RunReceiptRetention = NormalizeReceiptRetention(runReceiptRetention);
        RuntimeCleanupGrace = NormalizeRuntimeCleanupGrace(runtimeCleanupGrace);
        _timeProvider = TimeProvider.System;
    }

    internal DockerProvider(
        IDockerClient client,
        string? publishedHost = null,
        TimeSpan? runReceiptRetention = null,
        TimeSpan? runtimeCleanupGrace = null,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        PublishedHost = string.IsNullOrWhiteSpace(publishedHost) ? "127.0.0.1" : publishedHost.Trim();
        RunReceiptRetention = NormalizeReceiptRetention(runReceiptRetention);
        RuntimeCleanupGrace = NormalizeRuntimeCleanupGrace(runtimeCleanupGrace);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IDockerClient CreateClient() => _client;
    public string PublishedHost { get; }
    public TimeSpan RunReceiptRetention { get; }
    public TimeSpan RuntimeCleanupGrace { get; }
    internal DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public async Task<DockerContainerMetadata> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var fingerprint = DockerManager.ComputeRunFingerprint(config);
        var ownsNetwork = string.IsNullOrWhiteSpace(config.NetworkName);
        var networkName = ownsNetwork
            ? config.OperationId is { } operationId
                ? $"noctf-op-{operationId:N}"
                : $"noctf-{Guid.NewGuid():N}"
            : config.NetworkName!;
        var labels = WithManagedLabels(config.Labels, config.OperationId);
        if (config.OperationId.HasValue)
            labels[RequestFingerprintLabel] = fingerprint;
        labels[RuntimeContainerLabel] = bool.TrueString;
        if (ownsNetwork)
            labels[OwnedNetworkLabel] = networkName;
        if (config.Ttl is { } ttl && ttl > TimeSpan.Zero)
        {
            labels[RuntimeExpiryLabel] = UtcNow
                .Add(ttl)
                .Add(RuntimeCleanupGrace)
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        config = config with { Labels = labels };
        if (await TryRecoverContainerAsync(config, cancellationToken) is { } recovered)
            return recovered;

        var createdNetwork = false;
        if (ownsNetwork)
        {
            createdNetwork = await EnsureNetworkAsync(networkName, config.Labels, cancellationToken);
        }

        var networkedConfig = config with { NetworkName = networkName };
        var createParams = new CreateContainerParameters
        {
            Name = config.OperationId is { } id ? $"noctf-op-{id:N}" : null,
            Image = networkedConfig.Image,
            Cmd = string.IsNullOrWhiteSpace(networkedConfig.Command)
                ? null
                : networkedConfig.Entrypoint is { Count: > 0 }
                    ? [networkedConfig.Command]
                    : ["/bin/sh", "-c", networkedConfig.Command],
            Entrypoint = networkedConfig.Entrypoint?.ToList(),
            Env = networkedConfig.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = networkedConfig.Labels ?? new Dictionary<string, string>(),
            User = DockerHostConfigFactory.ResolveUser(networkedConfig),
            ExposedPorts = BuildExposedPorts(networkedConfig),
            HostConfig = DockerHostConfigFactory.Create(
                networkedConfig,
                publishAllPorts: false,
                BuildPortBindings(networkedConfig)),
            NetworkingConfig = BuildNetworkingConfig(networkName, networkedConfig.NetworkAliases)
        };

        string? containerId = null;
        try
        {
            await EnsureImageAsync(networkedConfig.Image, cancellationToken);

            var createResponse = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
            containerId = createResponse.ID;
            await _client.Containers.StartContainerAsync(containerId, null, cancellationToken);

            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            if (!inspect.State.Running)
            {
                throw RetryRequired(
                    containerId,
                    $"did not reach running state after creation (status: {inspect.State.Status ?? "unknown"})");
            }

            return new DockerContainerMetadata(
                ContainerId: containerId,
                Image: networkedConfig.Image,
                Status: inspect.State.Status,
                Ports: ReadPublishedPorts(inspect),
                NetworkName: ownsNetwork ? networkName : null,
                StartedAt: ResolveStartedAt(inspect)
            );
        }
        catch
        {
            DockerContainerMetadata? recoveredAfterFailure = null;
            if (!cancellationToken.IsCancellationRequested && config.OperationId.HasValue)
            {
                try
                {
                    using var recoveryCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    recoveredAfterFailure = await TryRecoverContainerAsync(config, recoveryCts.Token);
                }
                catch
                {
                    // Preserve the original create failure. Recovery is bounded
                    // and cleanup below still runs when recovery cannot prove a
                    // usable instance exists.
                }
            }

            if (recoveredAfterFailure is not null)
                return recoveredAfterFailure;

            if (!string.IsNullOrWhiteSpace(containerId))
                await RemoveContainerBestEffortAsync(containerId);
            if (ownsNetwork && createdNetwork)
                await RemoveNetworkBestEffortAsync(networkName);
            throw;
        }
    }

    internal async Task<DockerContainerMetadata?> TryRecoverContainerAsync(
        ContainerConfig config,
        CancellationToken cancellationToken)
    {
        if (config.OperationId is not { } operationId)
            return null;

        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{ManagedLabel}={bool.TrueString}"] = true,
                        [$"{OperationLabel}={operationId:D}"] = true
                    }
                }
            },
            cancellationToken);
        var existing = containers.OrderByDescending(container => container.Created).FirstOrDefault();
        if (existing is null)
            return null;

        var inspect = await _client.Containers.InspectContainerAsync(existing.ID, cancellationToken);
        ValidateRecoveredContainer(config, inspect);
        if (inspect.State.Running)
            return RecoveredMetadata(config, operationId, existing.ID, inspect);

        if (IsRemoving(inspect))
            throw RetryRequired(existing.ID, "is still being removed");

        if (IsDead(inspect))
        {
            await RemoveContainerForRecoveryAsync(existing.ID, cancellationToken);
            return null;
        }

        await _client.Containers.StartContainerAsync(existing.ID, null, cancellationToken);
        inspect = await _client.Containers.InspectContainerAsync(existing.ID, cancellationToken);
        ValidateRecoveredContainer(config, inspect);
        if (inspect.State.Running)
            return RecoveredMetadata(config, operationId, existing.ID, inspect);

        if (IsDead(inspect))
        {
            await RemoveContainerForRecoveryAsync(existing.ID, cancellationToken);
            return null;
        }

        if (IsRemoving(inspect))
            throw RetryRequired(existing.ID, "entered the removing state");

        throw RetryRequired(
            existing.ID,
            $"did not reach running state (status: {inspect.State.Status ?? "unknown"})");
    }

    private static DockerContainerMetadata RecoveredMetadata(
        ContainerConfig config,
        Guid operationId,
        string containerId,
        ContainerInspectResponse inspect)
        => new(
            ContainerId: containerId,
            Image: config.Image,
            Status: inspect.State.Status,
            Ports: ReadPublishedPorts(inspect),
            NetworkName: string.IsNullOrWhiteSpace(config.NetworkName)
                ? $"noctf-op-{operationId:N}"
                : null,
            StartedAt: ResolveStartedAt(inspect));

    private static bool IsDead(ContainerInspectResponse inspect)
        => string.Equals(inspect.State.Status, "dead", StringComparison.OrdinalIgnoreCase) ||
           inspect.State.Dead;

    private static bool IsRemoving(ContainerInspectResponse inspect)
        => string.Equals(inspect.State.Status, "removing", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException RetryRequired(string containerId, string reason)
        => new($"Docker container '{containerId}' {reason}; retry the Runner operation.");

    private async Task RemoveContainerForRecoveryAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
        }
    }

    private static void ValidateRecoveredContainer(ContainerConfig config, ContainerInspectResponse inspect)
    {
        if (!string.Equals(inspect.Config.Image, config.Image, StringComparison.Ordinal))
            throw new InvalidOperationException("Runner operation id was reused with a different container image.");

        if (config.Labels?.TryGetValue(RequestFingerprintLabel, out var expectedFingerprint) == true &&
            (inspect.Config.Labels?.TryGetValue(RequestFingerprintLabel, out var actualFingerprint) != true ||
             !string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Runner operation id was reused with a different container request.");
        }

        foreach (var identityKey in new[] { "competitionId", "teamId", "challengeId", "instanceId" })
        {
            if (config.Labels?.TryGetValue(identityKey, out var expected) == true &&
                (inspect.Config.Labels?.TryGetValue(identityKey, out var actual) != true ||
                 !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Runner operation id was reused with a different '{identityKey}' label.");
            }
        }
    }

    private async Task<bool> EnsureNetworkAsync(
        string networkName,
        IReadOnlyDictionary<string, string>? labels,
        CancellationToken cancellationToken)
    {
        if (await FindManagedNetworkAsync(networkName, labels, cancellationToken))
            return false;

        try
        {
            await _client.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = networkName,
                    Driver = "bridge",
                    Labels = labels is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string>(labels)
                },
                cancellationToken);
            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            if (await FindManagedNetworkAsync(networkName, labels, cancellationToken))
                return false;
            throw;
        }
    }

    private async Task<bool> FindManagedNetworkAsync(
        string networkName,
        IReadOnlyDictionary<string, string>? expectedLabels,
        CancellationToken cancellationToken)
    {
        var networks = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [networkName] = true }
                }
            },
            cancellationToken);
        var network = networks.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, networkName, StringComparison.Ordinal));
        if (network is null)
            return false;
        if (network.Labels is null ||
            !network.Labels.TryGetValue(ManagedLabel, out var managed) ||
            !bool.TryParse(managed, out var isManaged) ||
            !isManaged)
        {
            throw new InvalidOperationException($"Docker network '{networkName}' is not managed by NoCTF.");
        }

        if (expectedLabels?.TryGetValue(OperationLabel, out var expectedOperation) == true &&
            (!network.Labels.TryGetValue(OperationLabel, out var actualOperation) ||
             !string.Equals(actualOperation, expectedOperation, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Docker network '{networkName}' belongs to another operation.");
        }

        if (expectedLabels?.TryGetValue(RequestFingerprintLabel, out var expectedFingerprint) == true &&
            (!network.Labels.TryGetValue(RequestFingerprintLabel, out var actualFingerprint) ||
             !string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Docker network '{networkName}' belongs to a different request.");
        }

        return true;
    }

    private static DateTime? ResolveStartedAt(ContainerInspectResponse inspect)
        => inspect.Created > DateTime.UnixEpoch
            ? inspect.Created.ToUniversalTime()
            : null;

    private static IDictionary<string, EmptyStruct>? BuildExposedPorts(ContainerConfig config)
    {
        if (config.PortMappings is null || config.PortMappings.Count == 0) return null;
        return config.PortMappings.Keys.ToDictionary(port => $"{port}/tcp", _ => new EmptyStruct());
    }

    private static NetworkingConfig BuildNetworkingConfig(string networkName, IReadOnlyList<string>? aliases)
        => new()
        {
            EndpointsConfig = new Dictionary<string, EndpointSettings>
            {
                [networkName] = new()
                {
                    Aliases = aliases?
                        .Where(alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }
            }
        };

    private static IDictionary<string, IList<PortBinding>> BuildPortBindings(ContainerConfig config)
    {
        if (config.PortMappings is null || config.PortMappings.Count == 0)
            return new Dictionary<string, IList<PortBinding>>();

        return config.PortMappings.ToDictionary(
            kvp => $"{kvp.Key}/tcp",
            kvp => (IList<PortBinding>)new List<PortBinding>
            {
                new()
                {
                    HostPort = kvp.Value > 0 ? kvp.Value.ToString() : string.Empty
                }
            });
    }

    private static Dictionary<int, int> ReadPublishedPorts(ContainerInspectResponse inspect)
    {
        var result = new Dictionary<int, int>();
        if (inspect.NetworkSettings.Ports is null) return result;

        foreach (var (portKey, bindings) in inspect.NetworkSettings.Ports)
        {
            if (!int.TryParse(portKey.Split('/')[0], out var containerPort)) continue;

            var hostPortText = bindings?.FirstOrDefault()?.HostPort;
            if (!int.TryParse(hostPortText, out var hostPort) || hostPort <= 0) continue;

            result[containerPort] = hostPort;
        }

        return result;
    }

    private async Task EnsureImageAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>(),
                cancellationToken);
        }
    }

    public async Task DestroyContainerAsync(DockerContainerMetadata metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            var inspect = await _client.Containers.InspectContainerAsync(metadata.ContainerId, cancellationToken);
            if (!IsManagedNoCtfContainer(inspect))
                throw new InvalidOperationException($"Refusing to destroy unmanaged Docker container '{metadata.ContainerId}'.");
            EnsureRuntimeIdentity(inspect.Config.Labels, metadata);

            await _client.Containers.RemoveContainerAsync(
                metadata.ContainerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Destroy is idempotent from the platform's point of view; stale DB rows should not block recreation.
        }

        if (!string.IsNullOrWhiteSpace(metadata.NetworkName))
            await RemoveNetworkAsync(metadata.NetworkName, metadata, cancellationToken);
    }

    internal static Dictionary<string, string> WithManagedLabels(
        IReadOnlyDictionary<string, string>? labels,
        Guid? operationId = null)
    {
        var result = labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(labels, StringComparer.Ordinal);
        foreach (var key in result.Keys
                     .Where(IsRunnerOwnedLabel)
                     .ToArray())
        {
            result.Remove(key);
        }
        result[ManagedLabel] = bool.TrueString;
        if (operationId.HasValue)
            result[OperationLabel] = operationId.Value.ToString("D");
        return result;
    }

    internal static bool IsRunnerOwnedLabel(string key)
        => key.Equals(ManagedLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(OperationLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(RequestFingerprintLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(RunReceiptLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(RunDeadlineLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(RuntimeExpiryLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(RuntimeContainerLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(OwnedNetworkLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ComposeResourceLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ComposeProjectLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ComposeClaimLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ComposeMutexLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ComposeMutexOwnerLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ComposeMutexDeadlineLabel, StringComparison.OrdinalIgnoreCase);

    private static bool IsManagedNoCtfContainer(ContainerInspectResponse inspect)
        => inspect.Config.Labels is not null &&
           inspect.Config.Labels.TryGetValue(ManagedLabel, out var managed) &&
           bool.TryParse(managed, out var isManaged) &&
           isManaged &&
           inspect.Config.Labels.ContainsKey("competitionId");

    private async Task RemoveNetworkBestEffortAsync(string networkName)
    {
        if (!networkName.StartsWith("noctf-", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _client.Networks.DeleteNetworkAsync(networkName, cleanupCts.Token);
        }
        catch
        {
            // Network cleanup should not make container destroy non-idempotent.
        }
    }

    private async Task RemoveNetworkAsync(
        string networkName,
        DockerContainerMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!networkName.StartsWith("noctf-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to destroy unmanaged Docker network '{networkName}'.");

        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var networks = await _client.Networks.ListNetworksAsync(
                new NetworksListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [networkName] = true }
                    }
                },
                cleanupCts.Token);
            var network = networks.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, networkName, StringComparison.Ordinal));
            if (network is null)
                return;
            if (network.Labels is null ||
                !network.Labels.TryGetValue(ManagedLabel, out var managed) ||
                !bool.TryParse(managed, out var isManaged) ||
                !isManaged)
            {
                throw new InvalidOperationException($"Refusing to destroy unmanaged Docker network '{networkName}'.");
            }
            EnsureRuntimeIdentity(network.Labels, metadata);
            await _client.Networks.DeleteNetworkAsync(network.ID, cleanupCts.Token);
        }
        catch (DockerNetworkNotFoundException)
        {
        }
    }

    private static void EnsureRuntimeIdentity(
        IDictionary<string, string>? labels,
        DockerContainerMetadata metadata)
    {
        if (labels is null)
            throw new InvalidOperationException("Docker runtime has no identity labels.");
        EnsureIdentityLabel(labels, "competitionId", metadata.CompetitionId);
        EnsureIdentityLabel(labels, "teamId", metadata.TeamId);
        EnsureIdentityLabel(labels, "challengeId", metadata.ChallengeId);
    }

    private static void EnsureIdentityLabel(
        IDictionary<string, string> labels,
        string key,
        Guid? expected)
    {
        if (!expected.HasValue || expected.Value == Guid.Empty)
            return;
        if (!labels.TryGetValue(key, out var actual) ||
            !Guid.TryParse(actual, out var actualId) ||
            actualId != expected.Value)
        {
            throw new InvalidOperationException(
                "Docker runner refused to destroy a runtime whose identity does not match the request.");
        }
    }

    private async Task RemoveContainerBestEffortAsync(string containerId)
    {
        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cleanupCts.Token);
        }
        catch
        {
            // The create path is already failing; best-effort cleanup must not hide the original error.
        }
    }

    internal async Task<IAsyncDisposable> AcquireComposeMutationLeaseAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var mutexName = ComposeMutexName(projectName);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var owner = Guid.NewGuid().ToString("D");
            var deadline = UtcNow.AddHours(2);
            try
            {
                var response = await _client.Networks.CreateNetworkAsync(
                    new NetworksCreateParameters
                    {
                        Name = mutexName,
                        Driver = "bridge",
                        Labels = new Dictionary<string, string>
                        {
                            [ManagedLabel] = bool.TrueString,
                            [ComposeMutexLabel] = bool.TrueString,
                            [ComposeProjectLabel] = projectName,
                            [ComposeMutexOwnerLabel] = owner,
                            [ComposeMutexDeadlineLabel] = deadline
                                .ToUnixTimeMilliseconds()
                                .ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }
                    },
                    cancellationToken);
                return new DockerComposeMutationLease(this, response.ID);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                var mutex = await FindNetworkByExactNameAsync(mutexName, cancellationToken);
                if (mutex is null)
                    continue;
                RequireTrue(mutex.Labels, ManagedLabel, "compose mutation lock");
                RequireTrue(mutex.Labels, ComposeMutexLabel, "compose mutation lock");
                RequireLabel(
                    mutex.Labels,
                    ComposeProjectLabel,
                    projectName,
                    "compose mutation lock");
                if (!TryReadDeadline(
                        mutex.Labels,
                        ComposeMutexDeadlineLabel,
                        out var existingDeadline))
                {
                    throw new InvalidOperationException(
                        $"Docker compose project '{projectName}' has an invalid mutation lock.");
                }

                if (existingDeadline > UtcNow)
                    throw new InvalidOperationException(
                        $"Docker compose project '{projectName}' is being modified; retry the operation.");

                try
                {
                    await _client.Networks.DeleteNetworkAsync(mutex.ID, cancellationToken);
                }
                catch (DockerNetworkNotFoundException)
                {
                }
                catch (DockerApiException deleteException) when (
                    deleteException.StatusCode == HttpStatusCode.Conflict)
                {
                    throw new InvalidOperationException(
                        $"Docker compose project '{projectName}' is being modified; retry the operation.",
                        deleteException);
                }
            }
        }

        throw new InvalidOperationException(
            $"Docker compose project '{projectName}' mutation lock could not be acquired.");
    }

    private async ValueTask ReleaseComposeMutationLeaseAsync(
        string mutexId)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            // Docker IDs are immutable, so deleting by the ID returned from our
            // successful create cannot remove a replacement lock with the same
            // human-readable name.
            await _client.Networks.DeleteNetworkAsync(mutexId, cleanupCts.Token);
        }
        catch (DockerNetworkNotFoundException)
        {
        }
    }

    internal async Task<DockerComposeClaim> ClaimComposeProjectAsync(
        string projectName,
        IReadOnlyDictionary<string, string> requestedLabels,
        CancellationToken cancellationToken)
    {
        ValidateComposeRequestLabels(projectName, requestedLabels);
        var claim = await FindComposeClaimAsync(projectName, cancellationToken);
        if (claim is null)
        {
            var existingContainers = await ListComposeContainersAsync(projectName, cancellationToken);
            foreach (var container in existingContainers)
                ValidateComposeContainerForAdoption(container.Labels, requestedLabels);

            var claimLabels = new Dictionary<string, string>(requestedLabels, StringComparer.Ordinal)
            {
                [ComposeClaimLabel] = bool.TrueString
            };
            try
            {
                await _client.Networks.CreateNetworkAsync(
                    new NetworksCreateParameters
                    {
                        Name = ComposeClaimName(projectName),
                        Driver = "bridge",
                        Labels = claimLabels
                    },
                    cancellationToken);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Another Runner won the project claim. Its durable labels are
                // authoritative and are validated below.
            }

            claim = await FindComposeClaimAsync(projectName, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Docker compose project '{projectName}' claim could not be confirmed.");
        }

        ValidateComposeClaim(claim, projectName, requestedLabels);
        DateTimeOffset? runtimeDeadline = null;
        if (requestedLabels.ContainsKey(RuntimeExpiryLabel))
        {
            if (!TryReadDeadline(claim.Labels, RuntimeExpiryLabel, out var deadline))
                throw new InvalidOperationException(
                    $"Docker compose project '{projectName}' has an invalid runtime deadline.");
            runtimeDeadline = deadline;
        }
        else if (claim.Labels?.ContainsKey(RuntimeExpiryLabel) == true)
        {
            throw new InvalidOperationException(
                $"Docker compose project '{projectName}' belongs to a different request.");
        }

        return new DockerComposeClaim(runtimeDeadline);
    }

    internal async Task ValidateClaimedComposeProjectAsync(
        string projectName,
        IReadOnlyDictionary<string, string> expectedLabels,
        CancellationToken cancellationToken)
    {
        var claim = await FindComposeClaimAsync(projectName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Docker compose project '{projectName}' lost its lifecycle claim.");
        ValidateComposeClaim(claim, projectName, expectedLabels);

        var containers = await ListComposeContainersAsync(projectName, cancellationToken);
        if (containers.Count == 0)
            throw new InvalidOperationException(
                $"Docker compose project '{projectName}' did not create any service containers.");
        foreach (var container in containers)
            ValidateComposeRuntimeLabels(container.Labels, projectName, expectedLabels);
    }

    internal async Task<bool> ValidateComposeProjectForDownAsync(
        ComposeDeployment deployment,
        CancellationToken cancellationToken)
    {
        var expectedIdentity = ComposeIdentityLabels(deployment);
        var claim = await FindComposeClaimAsync(deployment.ProjectName, cancellationToken);
        var containers = await ListComposeContainersAsync(deployment.ProjectName, cancellationToken);
        if (claim is null)
        {
            if (containers.Count == 0)
                return false;

            // Backward-compatible cleanup for deployments created before durable
            // claims were introduced. At least one service container must prove
            // the complete identity; otherwise project-name reuse is unsafe.
            foreach (var container in containers)
            {
                EnsureIdentityWasNotOmitted(container.Labels, expectedIdentity);
                ValidateRuntimeIdentity(container.Labels, expectedIdentity);
            }
            return true;
        }

        ValidateComposeClaimForDown(claim, deployment.ProjectName, expectedIdentity);
        foreach (var container in containers)
        {
            EnsureIdentityWasNotOmitted(container.Labels, expectedIdentity);
            ValidateRuntimeIdentity(container.Labels, expectedIdentity);
            if (IsTrue(container.Labels, ManagedLabel))
            {
                RequireTrue(container.Labels, ComposeResourceLabel, "compose service");
                RequireLabel(container.Labels, ComposeProjectLabel, deployment.ProjectName, "compose service");
                if (claim.Labels?.TryGetValue(OperationLabel, out var operationId) == true)
                    RequireLabel(container.Labels, OperationLabel, operationId, "compose service");
                if (claim.Labels?.TryGetValue(RequestFingerprintLabel, out var fingerprint) == true)
                    RequireLabel(container.Labels, RequestFingerprintLabel, fingerprint, "compose service");
            }
        }

        return true;
    }

    internal async Task RemoveComposeClaimAsync(
        ComposeDeployment deployment,
        CancellationToken cancellationToken)
    {
        var claim = await FindComposeClaimAsync(deployment.ProjectName, cancellationToken);
        if (claim is null)
            return;

        ValidateComposeClaimForDown(
            claim,
            deployment.ProjectName,
            ComposeIdentityLabels(deployment));
        try
        {
            await _client.Networks.DeleteNetworkAsync(claim.ID, cancellationToken);
        }
        catch (DockerNetworkNotFoundException)
        {
        }
    }

    internal async Task EnsureComposeProjectRemovedAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var containers = await ListComposeContainersAsync(projectName, cancellationToken);
        var networks = await ListComposeProjectNetworksAsync(projectName, cancellationToken);
        var volumes = await ListComposeProjectVolumesAsync(projectName, cancellationToken);
        if (containers.Count != 0 || networks.Count != 0 || volumes.Count != 0)
        {
            throw new InvalidOperationException(
                $"Docker compose project '{projectName}' still owns runtime resources; retry cleanup.");
        }
    }

    internal static string ComposeClaimName(string projectName)
    {
        var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(projectName.ToLowerInvariant())))
            .ToLowerInvariant();
        return $"noctf-compose-claim-{hash[..24]}";
    }

    internal static string ComposeMutexName(string projectName)
    {
        var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(projectName.ToLowerInvariant())))
            .ToLowerInvariant();
        return $"noctf-compose-mutex-{hash[..24]}";
    }

    private async Task<NetworkResponse?> FindNetworkByExactNameAsync(
        string networkName,
        CancellationToken cancellationToken)
    {
        var networks = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [networkName] = true }
                }
            },
            cancellationToken);
        return networks.FirstOrDefault(network =>
            string.Equals(network.Name, networkName, StringComparison.Ordinal));
    }

    private async Task<NetworkResponse?> FindComposeClaimAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var claimName = ComposeClaimName(projectName);
        return await FindNetworkByExactNameAsync(claimName, cancellationToken);
    }

    private async Task<IList<ContainerListResponse>> ListComposeContainersAsync(
        string projectName,
        CancellationToken cancellationToken)
        => await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"com.docker.compose.project={projectName}"] = true
                    }
                }
            },
            cancellationToken);

    private static void ValidateComposeRequestLabels(
        string projectName,
        IReadOnlyDictionary<string, string> labels)
    {
        if (labels.Keys
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "Compose request labels cannot differ only by character casing.");
        }
        RequireTrue(labels, ManagedLabel, "compose request");
        RequireTrue(labels, ComposeResourceLabel, "compose request");
        RequireLabel(labels, ComposeProjectLabel, projectName, "compose request");
        if (!labels.TryGetValue(RequestFingerprintLabel, out var fingerprint) ||
            fingerprint.Length != 64 ||
            !fingerprint.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Compose request fingerprint is invalid.");
        }

        if (!labels.ContainsKey("competitionId"))
            throw new InvalidOperationException("Compose requests require a competition identity label.");
        ValidateGuidIdentityValue(labels, "competitionId");
        ValidateGuidIdentityValue(labels, "teamId");
        ValidateGuidIdentityValue(labels, "challengeId");
    }

    private static void ValidateGuidIdentityValue(
        IReadOnlyDictionary<string, string> labels,
        string key)
    {
        if (labels.TryGetValue(key, out var value) &&
            (!Guid.TryParse(value, out var id) || id == Guid.Empty))
        {
            throw new InvalidOperationException($"Compose identity label '{key}' is invalid.");
        }
    }

    private static void ValidateComposeClaim(
        NetworkResponse claim,
        string projectName,
        IReadOnlyDictionary<string, string> expectedLabels)
    {
        RequireTrue(claim.Labels, ManagedLabel, "compose claim");
        RequireTrue(claim.Labels, ComposeClaimLabel, "compose claim");
        RequireLabel(claim.Labels, ComposeProjectLabel, projectName, "compose claim");
        RequireLabel(
            claim.Labels,
            RequestFingerprintLabel,
            expectedLabels[RequestFingerprintLabel],
            "compose claim");

        var expectsOperation = expectedLabels.TryGetValue(OperationLabel, out var expectedOperation);
        string? actualOperation = null;
        var hasOperation = claim.Labels?.TryGetValue(OperationLabel, out actualOperation) == true;
        if (expectsOperation != hasOperation ||
            (expectsOperation && !string.Equals(
                actualOperation,
                expectedOperation,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Docker compose project '{projectName}' belongs to another operation.");
        }

        ValidateRuntimeIdentity(claim.Labels, IdentityLabels(expectedLabels));
    }

    private static void ValidateComposeClaimForDown(
        NetworkResponse claim,
        string projectName,
        IReadOnlyDictionary<string, string> expectedIdentity)
    {
        RequireTrue(claim.Labels, ManagedLabel, "compose claim");
        RequireTrue(claim.Labels, ComposeClaimLabel, "compose claim");
        RequireLabel(claim.Labels, ComposeProjectLabel, projectName, "compose claim");
        if (claim.Labels?.TryGetValue(RequestFingerprintLabel, out var fingerprint) != true ||
            string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new InvalidOperationException("Docker compose claim has no request fingerprint.");
        }
        EnsureIdentityWasNotOmitted(claim.Labels, expectedIdentity);
        ValidateRuntimeIdentity(claim.Labels, expectedIdentity);
    }

    private static void ValidateComposeContainerForAdoption(
        IEnumerable<KeyValuePair<string, string>>? actualLabels,
        IReadOnlyDictionary<string, string> expectedLabels)
    {
        ValidateRuntimeIdentity(actualLabels, IdentityLabels(expectedLabels));
        if (IsTrue(actualLabels, ManagedLabel))
        {
            RequireTrue(actualLabels, ComposeResourceLabel, "compose service");
            RequireLabel(
                actualLabels,
                ComposeProjectLabel,
                expectedLabels[ComposeProjectLabel],
                "compose service");
            RequireLabel(
                actualLabels,
                RequestFingerprintLabel,
                expectedLabels[RequestFingerprintLabel],
                "compose service");
            if (expectedLabels.TryGetValue(OperationLabel, out var operationId))
                RequireLabel(actualLabels, OperationLabel, operationId, "compose service");
        }
    }

    private static void ValidateComposeRuntimeLabels(
        IEnumerable<KeyValuePair<string, string>>? actualLabels,
        string projectName,
        IReadOnlyDictionary<string, string> expectedLabels)
    {
        RequireTrue(actualLabels, ManagedLabel, "compose service");
        RequireTrue(actualLabels, ComposeResourceLabel, "compose service");
        RequireLabel(actualLabels, ComposeProjectLabel, projectName, "compose service");
        RequireLabel(
            actualLabels,
            RequestFingerprintLabel,
            expectedLabels[RequestFingerprintLabel],
            "compose service");
        if (expectedLabels.TryGetValue(OperationLabel, out var operationId))
            RequireLabel(actualLabels, OperationLabel, operationId, "compose service");
        ValidateRuntimeIdentity(actualLabels, IdentityLabels(expectedLabels));
    }

    private static Dictionary<string, string> ComposeIdentityLabels(ComposeDeployment deployment)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["competitionId"] = deployment.CompetitionId.ToString("D")
        };
        if (deployment.TeamId is { } teamId && teamId != Guid.Empty)
            result["teamId"] = teamId.ToString("D");
        if (deployment.ChallengeId is { } challengeId && challengeId != Guid.Empty)
            result["challengeId"] = challengeId.ToString("D");
        return result;
    }

    private static Dictionary<string, string> IdentityLabels(
        IReadOnlyDictionary<string, string> labels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "competitionId", "teamId", "challengeId", "instanceId" })
        {
            if (labels.TryGetValue(key, out var value))
                result[key] = value;
        }
        return result;
    }

    private static void ValidateRuntimeIdentity(
        IEnumerable<KeyValuePair<string, string>>? actualLabels,
        IReadOnlyDictionary<string, string> expectedIdentity)
    {
        foreach (var (key, expectedValue) in expectedIdentity)
            RequireLabel(actualLabels, key, expectedValue, "compose runtime");
    }

    private static void EnsureIdentityWasNotOmitted(
        IEnumerable<KeyValuePair<string, string>>? actualLabels,
        IReadOnlyDictionary<string, string> expectedIdentity)
    {
        foreach (var key in new[] { "teamId", "challengeId" })
        {
            if (TryGetLabel(actualLabels, key, out _) && !expectedIdentity.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Docker compose runtime identity requires label '{key}'.");
            }
        }
    }

    private static bool IsTrue(
        IEnumerable<KeyValuePair<string, string>>? labels,
        string key)
        => TryGetLabel(labels, key, out var value) &&
           bool.TryParse(value, out var parsed) &&
           parsed;

    private static void RequireTrue(
        IEnumerable<KeyValuePair<string, string>>? labels,
        string key,
        string resource)
    {
        if (!IsTrue(labels, key))
            throw new InvalidOperationException(
                $"Docker {resource} is missing trusted lifecycle metadata.");
    }

    private static void RequireLabel(
        IEnumerable<KeyValuePair<string, string>>? labels,
        string key,
        string expectedValue,
        string resource)
    {
        if (!TryGetLabel(labels, key, out var actualValue) ||
            !string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Docker {resource} identity does not match label '{key}'.");
        }
    }

    private static bool TryReadDeadline(
        IEnumerable<KeyValuePair<string, string>>? labels,
        string key,
        out DateTimeOffset deadline)
    {
        deadline = default;
        return TryGetLabel(labels, key, out var value) &&
               long.TryParse(
                   value,
                   System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var timestamp) &&
               TryFromUnixTimestamp(timestamp, out deadline);
    }

    private static bool TryGetLabel(
        IEnumerable<KeyValuePair<string, string>>? labels,
        string key,
        out string value)
    {
        if (labels is not null)
        {
            foreach (var pair in labels)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    public async Task CleanupExpiredRunReceiptsAsync(CancellationToken cancellationToken = default)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{ManagedLabel}={bool.TrueString}"] = true,
                        [$"{RunReceiptLabel}={bool.TrueString}"] = true
                    }
                }
            },
            cancellationToken);

        foreach (var container in containers)
        {
            ContainerInspectResponse inspect;
            try
            {
                inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
            }
            catch (DockerContainerNotFoundException)
            {
                continue;
            }

            if (!TryReadRunReceiptIdentity(inspect, out var deadline))
                continue;

            var now = UtcNow;
            if (IsRunningOrRestarting(inspect))
            {
                if (deadline <= now)
                    await KillRunContainerAsync(container.ID, cancellationToken);
                continue;
            }

            var completedAt = ParseDockerTimestamp(inspect.State.FinishedAt);
            var retentionStartedAt = completedAt is { } finished && finished > DateTimeOffset.UnixEpoch
                ? finished
                : deadline;
            if (retentionStartedAt + RunReceiptRetention > now)
                continue;

            try
            {
                await _client.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true },
                    cancellationToken);
            }
            catch (DockerContainerNotFoundException)
            {
            }
        }


        await CleanupExpiredRuntimeContainersAsync(cancellationToken);
        await CleanupExpiredRuntimeNetworksAsync(cancellationToken);
        await CleanupExpiredComposeProjectsAsync(cancellationToken);
        await CleanupExpiredComposeMutexesAsync(cancellationToken);
    }

    private async Task CleanupExpiredRuntimeContainersAsync(CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{ManagedLabel}={bool.TrueString}"] = true,
                        [$"{RuntimeContainerLabel}={bool.TrueString}"] = true,
                        [RuntimeExpiryLabel] = true
                    }
                }
            },
            cancellationToken);

        foreach (var container in containers)
        {
            ContainerInspectResponse inspect;
            try
            {
                inspect = await _client.Containers.InspectContainerAsync(
                    container.ID,
                    cancellationToken);
            }
            catch (DockerContainerNotFoundException)
            {
                continue;
            }

            if (!TryReadManagedRuntimeExpiry(inspect.Config.Labels, out var deadline) ||
                deadline > UtcNow)
            {
                continue;
            }

            try
            {
                await _client.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true },
                    cancellationToken);
            }
            catch (DockerContainerNotFoundException)
            {
            }
        }
    }

    private async Task CleanupExpiredRuntimeNetworksAsync(CancellationToken cancellationToken)
    {
        var networks = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{ManagedLabel}={bool.TrueString}"] = true,
                        [$"{RuntimeContainerLabel}={bool.TrueString}"] = true,
                        [RuntimeExpiryLabel] = true
                    }
                }
            },
            cancellationToken);

        foreach (var network in networks)
        {
            if (!TryReadManagedRuntimeExpiry(network.Labels, out var deadline) ||
                deadline > UtcNow ||
                !TryGetLabel(network.Labels, OwnedNetworkLabel, out var ownedNetwork) ||
                !string.Equals(ownedNetwork, network.Name, StringComparison.Ordinal) ||
                !network.Name.StartsWith("noctf-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await _client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
            }
            catch (DockerNetworkNotFoundException)
            {
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // A container removal may still be settling. The network keeps
                // its deadline and will be retried on the next cleanup pass.
            }
        }
    }

    private static bool TryReadManagedRuntimeExpiry(
        IEnumerable<KeyValuePair<string, string>>? labels,
        out DateTimeOffset deadline)
    {
        deadline = default;
        return IsTrue(labels, ManagedLabel) &&
               IsTrue(labels, RuntimeContainerLabel) &&
               !IsTrue(labels, RunReceiptLabel) &&
               TryGetLabel(labels, "competitionId", out var competitionValue) &&
               Guid.TryParse(competitionValue, out var competitionId) &&
               competitionId != Guid.Empty &&
               TryReadDeadline(labels, RuntimeExpiryLabel, out deadline);
    }

    private async Task CleanupExpiredComposeMutexesAsync(CancellationToken cancellationToken)
    {
        var networks = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{ManagedLabel}={bool.TrueString}"] = true,
                        [$"{ComposeMutexLabel}={bool.TrueString}"] = true
                    }
                }
            },
            cancellationToken);
        foreach (var network in networks)
        {
            if (!IsTrue(network.Labels, ManagedLabel) ||
                !IsTrue(network.Labels, ComposeMutexLabel) ||
                !TryGetLabel(network.Labels, ComposeProjectLabel, out var projectName) ||
                !string.Equals(network.Name, ComposeMutexName(projectName), StringComparison.Ordinal) ||
                !TryReadDeadline(network.Labels, ComposeMutexDeadlineLabel, out var deadline) ||
                deadline > UtcNow)
            {
                continue;
            }

            try
            {
                await _client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
            }
            catch (DockerNetworkNotFoundException)
            {
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
            }
        }
    }

    private async Task CleanupExpiredComposeProjectsAsync(CancellationToken cancellationToken)
    {
        var claims = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{ManagedLabel}={bool.TrueString}"] = true,
                        [$"{ComposeClaimLabel}={bool.TrueString}"] = true,
                        [RuntimeExpiryLabel] = true
                    }
                }
            },
            cancellationToken);

        foreach (var candidate in claims)
        {
            if (!TryValidateExpiredComposeClaim(candidate, out var projectName))
                continue;

            try
            {
                await using var mutationLease = await AcquireComposeMutationLeaseAsync(
                    projectName,
                    cancellationToken);
                var claim = await FindComposeClaimAsync(projectName, cancellationToken);
                if (claim is null ||
                    !string.Equals(claim.ID, candidate.ID, StringComparison.Ordinal) ||
                    !TryValidateExpiredComposeClaim(claim, out _))
                {
                    continue;
                }

                var expectedLabels = new Dictionary<string, string>(claim.Labels, StringComparer.Ordinal);
                var containers = await ListComposeContainersAsync(projectName, cancellationToken);
                foreach (var container in containers)
                    ValidateComposeRuntimeLabels(container.Labels, projectName, expectedLabels);

                var networks = await ListComposeProjectNetworksAsync(projectName, cancellationToken);
                foreach (var network in networks)
                    ValidateComposeRuntimeLabels(network.Labels, projectName, expectedLabels);

                var volumes = await ListComposeProjectVolumesAsync(projectName, cancellationToken);
                foreach (var volume in volumes)
                    ValidateComposeRuntimeLabels(volume.Labels, projectName, expectedLabels);

                foreach (var container in containers)
                {
                    try
                    {
                        await _client.Containers.RemoveContainerAsync(
                            container.ID,
                            new ContainerRemoveParameters { Force = true },
                            cancellationToken);
                    }
                    catch (DockerContainerNotFoundException)
                    {
                    }
                }

                foreach (var network in networks)
                {
                    try
                    {
                        await _client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
                    }
                    catch (DockerNetworkNotFoundException)
                    {
                    }
                }

                foreach (var volume in volumes)
                {
                    try
                    {
                        await _client.Volumes.RemoveAsync(volume.Name, true, cancellationToken);
                    }
                    catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                    }
                }

                await _client.Networks.DeleteNetworkAsync(claim.ID, cancellationToken);
            }
            catch (DockerApiException ex) when (
                ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                // Resource teardown can race Docker's endpoint cleanup. The
                // durable claim remains discoverable for the next pass.
            }
            catch (InvalidOperationException)
            {
                // A concurrent mutation or inconsistent resource identity must
                // never be bypassed by the background collector.
            }
        }
    }

    private bool TryValidateExpiredComposeClaim(
        NetworkResponse claim,
        out string projectName)
    {
        projectName = string.Empty;
        return IsTrue(claim.Labels, ManagedLabel) &&
               IsTrue(claim.Labels, ComposeClaimLabel) &&
               TryGetLabel(claim.Labels, ComposeProjectLabel, out projectName) &&
               !string.IsNullOrWhiteSpace(projectName) &&
               string.Equals(claim.Name, ComposeClaimName(projectName), StringComparison.Ordinal) &&
               TryGetLabel(claim.Labels, RequestFingerprintLabel, out var fingerprint) &&
               fingerprint.Length == 64 &&
               TryGetLabel(claim.Labels, "competitionId", out var competitionValue) &&
               Guid.TryParse(competitionValue, out var competitionId) &&
               competitionId != Guid.Empty &&
               TryReadDeadline(claim.Labels, RuntimeExpiryLabel, out var deadline) &&
               deadline <= UtcNow;
    }

    private async Task<IList<NetworkResponse>> ListComposeProjectNetworksAsync(
        string projectName,
        CancellationToken cancellationToken)
        => await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"com.docker.compose.project={projectName}"] = true
                    }
                }
            },
            cancellationToken);

    private async Task<IList<VolumeResponse>> ListComposeProjectVolumesAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var response = await _client.Volumes.ListAsync(
            new VolumesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"com.docker.compose.project={projectName}"] = true
                    }
                }
            },
            cancellationToken);
        return response.Volumes ?? [];
    }

    internal bool IsExpiredRunReceipt(ContainerInspectResponse inspect)
    {
        if (!TryReadRunReceiptIdentity(inspect, out var deadline) ||
            IsRunningOrRestarting(inspect))
        {
            return false;
        }

        var completedAt = ParseDockerTimestamp(inspect.State.FinishedAt);
        var retentionStartedAt = completedAt is { } finished && finished > DateTimeOffset.UnixEpoch
            ? finished
            : deadline;
        return retentionStartedAt + RunReceiptRetention <= UtcNow;
    }

    internal static bool TryReadRunDeadline(
        ContainerInspectResponse inspect,
        out DateTimeOffset deadline)
    {
        deadline = default;
        return inspect.Config.Labels?.TryGetValue(RunDeadlineLabel, out var value) == true &&
               long.TryParse(value, System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out var timestamp) &&
               TryFromUnixTimestamp(timestamp, out deadline);
    }

    private static bool TryReadRunReceiptIdentity(
        ContainerInspectResponse inspect,
        out DateTimeOffset deadline)
    {
        deadline = default;
        var labels = inspect.Config.Labels;
        return labels is not null &&
               labels.TryGetValue(ManagedLabel, out var managed) &&
               bool.TryParse(managed, out var isManaged) &&
               isManaged &&
               labels.TryGetValue(RunReceiptLabel, out var receipt) &&
               bool.TryParse(receipt, out var isReceipt) &&
               isReceipt &&
               labels.TryGetValue(OperationLabel, out var operationValue) &&
               Guid.TryParse(operationValue, out var operationId) &&
               labels.ContainsKey(RequestFingerprintLabel) &&
               string.Equals(
                   inspect.Name?.TrimStart('/'),
                   $"noctf-run-{operationId:N}",
                   StringComparison.Ordinal) &&
               TryReadRunDeadline(inspect, out deadline);
    }

    private async Task KillRunContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.KillContainerAsync(
                containerId,
                new ContainerKillParameters(),
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // The process may have exited between inspect and kill. It remains
            // available as a receipt and will be collected on the next pass.
        }
    }

    private static bool IsRunningOrRestarting(ContainerInspectResponse inspect)
        => inspect.State.Running ||
           string.Equals(inspect.State.Status, "restarting", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseDockerTimestamp(string? value)
        => DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? timestamp.ToUniversalTime()
            : null;

    private static bool TryFromUnixTimestamp(long timestamp, out DateTimeOffset value)
    {
        try
        {
            value = Math.Abs(timestamp) >= 100_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : DateTimeOffset.FromUnixTimeSeconds(timestamp);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static TimeSpan NormalizeReceiptRetention(TimeSpan? retention)
    {
        var value = retention is { } configured && configured > TimeSpan.Zero
            ? configured
            : TimeSpan.FromMinutes(30);
        return value > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : value;
    }

    private static TimeSpan NormalizeRuntimeCleanupGrace(TimeSpan? grace)
    {
        var value = grace is { } configured && configured > TimeSpan.Zero
            ? configured
            : TimeSpan.FromDays(7);
        return value > TimeSpan.FromDays(30) ? TimeSpan.FromDays(30) : value;
    }

    private sealed class DockerComposeMutationLease(
        DockerProvider owner,
        string mutexId) : IAsyncDisposable
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                await owner.ReleaseComposeMutationLeaseAsync(
                    mutexId);
            }
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _configuration?.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed record DockerComposeClaim(DateTimeOffset? RuntimeDeadline);
