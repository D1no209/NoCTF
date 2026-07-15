using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.PluginBase;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoCTF.Container.Docker;

internal delegate Task<string> DockerComposeCommandRunner(
    string composeYaml,
    string projectName,
    IReadOnlyList<string> arguments,
    Dictionary<string, string>? environmentVariables,
    CancellationToken cancellationToken);

public class DockerManager : IContainerManager
{
    private static readonly ComposeProjectLock ComposeLocks = new();
    private readonly DockerProvider provider;
    private readonly DockerComposeCommandRunner composeCommandRunner;

    public DockerManager(DockerProvider provider)
        : this(provider, DockerComposeRunner.RunAsync)
    {
    }

    internal DockerManager(
        DockerProvider provider,
        DockerComposeCommandRunner composeCommandRunner)
    {
        this.provider = provider;
        this.composeCommandRunner = composeCommandRunner;
    }

    public async Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var metadata = await provider.CreateContainerAsync(config, cancellationToken);
        var startedAt = metadata.StartedAt ?? provider.UtcNow.UtcDateTime;

        return new ContainerInstance(
            Id: Guid.NewGuid(),
            CompetitionId: config.Labels?.TryGetValue("competitionId", out var compId) == true && Guid.TryParse(compId, out var c) ? c : Guid.Empty,
            TeamId: config.Labels?.TryGetValue("teamId", out var teamId) == true && Guid.TryParse(teamId, out var t) ? t : null,
            ChallengeId: config.Labels?.TryGetValue("challengeId", out var challId) == true && Guid.TryParse(challId, out var ch) ? ch : null,
            ProviderType: "docker",
            ContainerId: metadata.ContainerId,
            PortMappings: metadata.Ports,
            Status: metadata.Status,
            StartedAt: startedAt,
            ExpectedStopAt: config.Ttl.HasValue ? startedAt.Add(config.Ttl.Value) : null,
            OrchestrationNamespace: metadata.NetworkName,
            InternalHost: provider.PublishedHost,
            InternalPortMappings: metadata.Ports
        );
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
    {
        var metadata = new DockerContainerMetadata(
            container.ContainerId,
            "",
            container.Status,
            container.PortMappings,
            container.OrchestrationNamespace,
            CompetitionId: container.CompetitionId,
            TeamId: container.TeamId,
            ChallengeId: container.ChallengeId
        );
        await provider.DestroyContainerAsync(metadata, cancellationToken);
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var effectiveTtl = ResolveRunTimeout(config.Ttl);
        var requestedAt = provider.UtcNow;
        var requestDeadline = requestedAt.Add(effectiveTtl);
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(effectiveTtl);
        var runCt = runCts.Token;
        var fingerprint = ComputeRunFingerprint(config);
        var labels = DockerProvider.WithManagedLabels(config.Labels, config.OperationId);
        if (config.OperationId.HasValue)
        {
            labels[DockerProvider.RequestFingerprintLabel] = fingerprint;
            labels[DockerProvider.RunReceiptLabel] = bool.TrueString;
            labels[DockerProvider.RunDeadlineLabel] = requestDeadline
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        config = config with { Labels = labels };
        var client = provider.CreateClient();

        var createParams = new CreateContainerParameters
        {
            Name = config.OperationId is { } operationId ? $"noctf-run-{operationId:N}" : null,
            Image = config.Image,
            Cmd = string.IsNullOrWhiteSpace(config.Command)
                ? null
                : config.Entrypoint is { Count: > 0 }
                    ? [config.Command]
                    : ["/bin/sh", "-c", config.Command],
            Entrypoint = config.Entrypoint?.ToList(),
            Env = config.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = config.Labels ?? new Dictionary<string, string>(),
            User = DockerHostConfigFactory.ResolveUser(config),
            HostConfig = DockerHostConfigFactory.Create(config, publishAllPorts: false),
            NetworkingConfig = BuildNetworkingConfig(config)
        };

        var startedAt = requestedAt.UtcDateTime;
        string? containerId = null;
        var removeOnExit = !config.OperationId.HasValue;
        try
        {
            var existing = await TryGetExistingRunContainerAsync(client, config, fingerprint, runCt);
            if (existing is not null)
            {
                containerId = existing.ID;
                startedAt = ResolveStartedAt(existing, startedAt);
                var deadline = DockerProvider.TryReadRunDeadline(existing, out var persistedDeadline)
                    ? persistedDeadline
                    : new DateTimeOffset(startedAt, TimeSpan.Zero).Add(effectiveTtl);

                if (provider.IsExpiredRunReceipt(existing))
                {
                    await RemoveRunContainerAsync(client, containerId);
                    containerId = null;
                    existing = null;
                }
                else if (string.Equals(existing.State.Status, "removing", StringComparison.OrdinalIgnoreCase))
                {
                    throw RunRetryRequired(containerId, "is still being removed");
                }
                else if (IsTerminal(existing))
                {
                    return await BuildTerminalResultAsync(
                        client,
                        existing,
                        containerId,
                        startedAt,
                        deadline,
                        runCt);
                }
                else if (deadline <= provider.UtcNow)
                {
                    if (existing.State.Running ||
                        string.Equals(existing.State.Status, "restarting", StringComparison.OrdinalIgnoreCase))
                    {
                        await KillTimedOutRunContainerAsync(client, containerId);
                    }
                    return TimeoutResult(containerId, startedAt, provider.UtcNow.UtcDateTime);
                }
                else if (!existing.State.Running &&
                         string.Equals(existing.State.Status, "created", StringComparison.OrdinalIgnoreCase))
                {
                    await client.Containers.StartContainerAsync(containerId, null, runCt);
                    existing = await client.Containers.InspectContainerAsync(containerId, runCt);
                    if (IsTerminal(existing))
                    {
                        return await BuildTerminalResultAsync(
                            client,
                            existing,
                            containerId,
                            startedAt,
                            deadline,
                            runCt);
                    }
                    if (!existing.State.Running)
                    {
                        throw RunRetryRequired(
                            containerId,
                            $"did not reach running state (status: {existing.State.Status ?? "unknown"})");
                    }
                }
                else if (!existing.State.Running)
                {
                    throw RunRetryRequired(
                        containerId,
                        $"is not yet terminal (status: {existing.State.Status ?? "unknown"})");
                }
            }

            if (containerId is null)
            {
                await EnsureImageAsync(client, config.Image, runCt);
                try
                {
                    var createResponse = await client.Containers.CreateContainerAsync(createParams, runCt);
                    containerId = createResponse.ID;
                    startedAt = provider.UtcNow.UtcDateTime;
                    await client.Containers.StartContainerAsync(containerId, null, runCt);
                    existing = await client.Containers.InspectContainerAsync(containerId, runCt);
                    if (IsTerminal(existing))
                    {
                        return await BuildTerminalResultAsync(
                            client,
                            existing,
                            containerId,
                            startedAt,
                            requestDeadline,
                            runCt);
                    }
                    if (!existing.State.Running)
                    {
                        throw RunRetryRequired(
                            containerId,
                            $"did not reach running state after creation (status: {existing.State.Status ?? "unknown"})");
                    }
                }
                catch (DockerApiException ex) when (
                    ex.StatusCode == System.Net.HttpStatusCode.Conflict &&
                    config.OperationId.HasValue)
                {
                    throw RunRetryRequired(createParams.Name, "is being created by another Runner request");
                }
            }

            ContainerWaitResponse waitResponse;
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(runCt);
            var activeDeadline = existing is not null &&
                                 DockerProvider.TryReadRunDeadline(existing, out var persistedActiveDeadline)
                ? persistedActiveDeadline
                : requestDeadline;
            var remaining = activeDeadline - provider.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                await KillTimedOutRunContainerAsync(client, containerId);
                return TimeoutResult(containerId, startedAt, provider.UtcNow.UtcDateTime);
            }
            waitCts.CancelAfter(remaining);
            try
            {
                waitResponse = await client.Containers.WaitContainerAsync(containerId, waitCts.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                waitCts.IsCancellationRequested)
            {
                await KillTimedOutRunContainerAsync(client, containerId);
                return TimeoutResult(containerId, startedAt, provider.UtcNow.UtcDateTime);
            }
            if (config.OperationId.HasValue)
            {
                var completed = await client.Containers.InspectContainerAsync(containerId, runCt);
                if (IsTerminal(completed))
                {
                    return await BuildTerminalResultAsync(
                        client,
                        completed,
                        containerId,
                        startedAt,
                        activeDeadline,
                        runCt);
                }
            }
            var finishedAt = provider.UtcNow.UtcDateTime;
            var logs = await ReadRunLogsBestEffortAsync(client, containerId, runCt);

            return new ContainerRunResult(
                ContainerId: containerId,
                ExitCode: (int)waitResponse.StatusCode,
                StdOut: logs.StdOut,
                StdErr: logs.StdErr,
                StartedAt: startedAt,
                FinishedAt: finishedAt
            );
        }
        finally
        {
            if (removeOnExit && !string.IsNullOrWhiteSpace(containerId))
                await RemoveRunContainerAsync(client, containerId);
        }
    }

    private static async Task EnsureImageAsync(
        IDockerClient client,
        string image,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            await client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>(),
                cancellationToken);
        }
    }

    private static async Task<ContainerInspectResponse?> TryGetExistingRunContainerAsync(
        IDockerClient client,
        ContainerConfig config,
        string fingerprint,
        CancellationToken ct)
    {
        if (config.OperationId is not { } operationId)
            return null;

        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool>
                    {
                        [$"noctf-run-{operationId:N}"] = true
                    },
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{DockerProvider.ManagedLabel}={bool.TrueString}"] = true,
                        [$"{DockerProvider.OperationLabel}={operationId:D}"] = true
                    }
                }
            },
            ct);
        var candidate = containers.OrderByDescending(item => item.Created).FirstOrDefault();
        if (candidate is null)
            return null;

        var inspect = await client.Containers.InspectContainerAsync(candidate.ID, ct);
        if (!string.Equals(inspect.Config.Image, config.Image, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(inspect.Name) &&
             !string.Equals(
                 inspect.Name.TrimStart('/'),
                 $"noctf-run-{operationId:N}",
                 StringComparison.Ordinal)) ||
            inspect.Config.Labels?.TryGetValue(DockerProvider.OperationLabel, out var actualOperationId) != true ||
            !string.Equals(actualOperationId, operationId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            inspect.Config.Labels.TryGetValue(DockerProvider.RequestFingerprintLabel, out var actualFingerprint) != true ||
            !string.Equals(actualFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Runner one-shot operation id was reused with a different request.");
        }

        inspect.ID ??= candidate.ID;
        return inspect;
    }

    private static async Task<(string? StdOut, string? StdErr)> ReadRunLogsBestEffortAsync(
        IDockerClient client,
        string containerId,
        CancellationToken ct)
    {
        try
        {
            var logsParams = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Tail = "100"
            };
            using var logStream = await client.Containers.GetContainerLogsAsync(containerId, false, logsParams, ct);
            return await ReadBoundedOutputAsync(logStream, ct);
        }
        catch
        {
            return (null, null);
        }
    }

    private static async Task<ContainerRunResult> BuildTerminalResultAsync(
        IDockerClient client,
        ContainerInspectResponse inspect,
        string containerId,
        DateTime startedAt,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var finishedAt = ParseDockerTimestamp(inspect.State.FinishedAt, DateTime.UtcNow);
        if (new DateTimeOffset(finishedAt.ToUniversalTime()) >= deadline)
            return TimeoutResult(containerId, startedAt, finishedAt);

        var logs = await ReadRunLogsBestEffortAsync(client, containerId, cancellationToken);
        return new ContainerRunResult(
            ContainerId: containerId,
            ExitCode: (int)inspect.State.ExitCode,
            StdOut: logs.StdOut,
            StdErr: logs.StdErr,
            StartedAt: startedAt,
            FinishedAt: finishedAt);
    }

    private static bool IsTerminal(ContainerInspectResponse inspect)
        => inspect.State.Dead ||
           string.Equals(inspect.State.Status, "dead", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(inspect.State.Status, "exited", StringComparison.OrdinalIgnoreCase);

    private static DateTime ResolveStartedAt(ContainerInspectResponse inspect, DateTime fallback)
        => inspect.Created > DateTime.UnixEpoch
            ? inspect.Created.ToUniversalTime()
            : fallback;

    private static ContainerRunResult TimeoutResult(
        string containerId,
        DateTime startedAt,
        DateTime finishedAt)
        => new(
            ContainerId: containerId,
            ExitCode: 124,
            StdOut: null,
            StdErr: "Container execution exceeded its configured TTL.",
            StartedAt: startedAt,
            FinishedAt: finishedAt);

    private static DateTime ParseDockerTimestamp(string? value, DateTime fallback)
        => DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.UtcDateTime
            : fallback;

    private static InvalidOperationException RunRetryRequired(string? containerId, string reason)
        => new($"Docker one-shot container '{containerId ?? "unknown"}' {reason}; retry the Runner operation.");

    internal static string ComputeRunFingerprint(ContainerConfig config)
    {
        var request = new
        {
            config.Image,
            config.Command,
            Environment = config.EnvironmentVariables?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            Labels = config.Labels?
                .Where(pair => !DockerProvider.IsRunnerOwnedLabel(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray(),
            Ports = config.PortMappings?.OrderBy(pair => pair.Key).ToArray(),
            config.NetworkName,
            config.RegistryAuth,
            TtlTicks = config.Ttl?.Ticks,
            config.ResourceLimits,
            config.SecurityPolicy,
            Entrypoint = config.Entrypoint?.ToArray(),
            config.OrchestrationJson,
            NetworkAliases = config.NetworkAliases?
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(request);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    private static NetworkingConfig? BuildNetworkingConfig(ContainerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.NetworkName))
            return null;

        return new NetworkingConfig
        {
            EndpointsConfig = new Dictionary<string, EndpointSettings>
            {
                [config.NetworkName] = new()
                {
                    Aliases = config.NetworkAliases?
                        .Where(alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }
            }
        };
    }

    internal static async Task RemoveRunContainerAsync(IDockerClient client, string containerId)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cleanupCts.Token);
        }
        catch (DockerContainerNotFoundException)
        {
        }
    }

    private static async Task KillTimedOutRunContainerAsync(
        IDockerClient client,
        string containerId)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await client.Containers.KillContainerAsync(
                containerId,
                new ContainerKillParameters(),
                cleanupCts.Token);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // It exited between the wait timeout and kill request.
        }
    }

    internal static async Task<(string? StdOut, string? StdErr)> ReadBoundedOutputAsync(
        MultiplexedStream stream,
        CancellationToken cancellationToken)
    {
        var capture = new BoundedDockerLogCapture();
        var buffer = new byte[8_192];
        while (capture.RemainingReadBytes > 0)
        {
            var read = await stream.ReadOutputAsync(
                buffer,
                0,
                Math.Min(buffer.Length, capture.RemainingReadBytes),
                cancellationToken);
            if (read.EOF)
                break;
            if (read.Count <= 0)
                break;

            capture.Append(read.Target, buffer.AsSpan(0, read.Count));
        }

        return capture.GetText();
    }

    private static TimeSpan ResolveRunTimeout(TimeSpan? requested)
    {
        var timeout = requested is { } value && value > TimeSpan.Zero
            ? value
            : TimeSpan.FromMinutes(15);
        return timeout > TimeSpan.FromHours(24)
            ? TimeSpan.FromHours(24)
            : timeout;
    }

    public async Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
    {
        DockerComposeRunner.ValidateProjectName(config.ProjectName);
        DockerComposeRunner.ValidateComposeYaml(config.ComposeYaml);
        DockerComposeRunner.ValidateProcessEnvironment(config.EnvironmentVariables);

        await using var projectLock = await ComposeLocks.AcquireAsync(
            config.ProjectName,
            cancellationToken);
        await using var mutationLease = await provider.AcquireComposeMutationLeaseAsync(
            config.ProjectName,
            cancellationToken);
        var fingerprint = ComputeComposeFingerprint(config);
        var runtimeLabels = BuildComposeRuntimeLabels(config, fingerprint);
        var claim = await provider.ClaimComposeProjectAsync(
            config.ProjectName,
            runtimeLabels,
            cancellationToken);
        if (claim.RuntimeDeadline is { } deadline)
        {
            if (deadline <= provider.UtcNow)
                throw new InvalidOperationException(
                    $"Compose project '{config.ProjectName}' has expired and must be destroyed before reuse.");
            runtimeLabels[DockerProvider.RuntimeExpiryLabel] = deadline
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var effectiveComposeYaml = DockerComposeRunner.ApplyRuntimeLabels(
            config.ComposeYaml,
            runtimeLabels);
        DockerComposeRunner.ValidateComposeYaml(effectiveComposeYaml);

        await composeCommandRunner(
            effectiveComposeYaml,
            config.ProjectName,
            ["up", "-d", "--remove-orphans"],
            config.EnvironmentVariables,
            cancellationToken);
        await provider.ValidateClaimedComposeProjectAsync(
            config.ProjectName,
            runtimeLabels,
            cancellationToken);

        var startedAt = provider.UtcNow.UtcDateTime;

        return new ComposeDeployment(
            Id: Guid.NewGuid(),
            CompetitionId: config.Labels?.TryGetValue("competitionId", out var compId) == true && Guid.TryParse(compId, out var c) ? c : Guid.Empty,
            TeamId: config.Labels?.TryGetValue("teamId", out var teamId) == true && Guid.TryParse(teamId, out var t) ? t : null,
            ChallengeId: config.Labels?.TryGetValue("challengeId", out var challId) == true && Guid.TryParse(challId, out var ch) ? ch : null,
            ProviderType: "docker-compose",
            ProjectName: config.ProjectName,
            ComposeYaml: effectiveComposeYaml,
            Status: "running",
            StartedAt: startedAt,
            ExpectedStopAt: claim.RuntimeDeadline?.UtcDateTime
        );
    }

    public async Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
    {
        DockerComposeRunner.ValidateProjectName(deployment.ProjectName);
        DockerComposeRunner.ValidateComposeYaml(deployment.ComposeYaml);
        if (!deployment.ProjectName.StartsWith("noctf-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to destroy unmanaged compose project '{deployment.ProjectName}'.");

        await using var projectLock = await ComposeLocks.AcquireAsync(
            deployment.ProjectName,
            cancellationToken);
        await using var mutationLease = await provider.AcquireComposeMutationLeaseAsync(
            deployment.ProjectName,
            cancellationToken);
        var projectExists = await provider.ValidateComposeProjectForDownAsync(
            deployment,
            cancellationToken);
        if (!projectExists)
            return;

        await composeCommandRunner(
            deployment.ComposeYaml,
            deployment.ProjectName,
            ["down", "--remove-orphans", "--volumes"],
            null,
            cancellationToken);
        await provider.EnsureComposeProjectRemovedAsync(
            deployment.ProjectName,
            cancellationToken);
        await provider.RemoveComposeClaimAsync(deployment, cancellationToken);
    }

    internal static string ComputeComposeFingerprint(ComposeConfig config)
    {
        var request = new
        {
            config.ProjectName,
            config.ComposeYaml,
            Environment = config.EnvironmentVariables?
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray(),
            Labels = config.Labels?
                .Where(pair => !DockerProvider.IsRunnerOwnedLabel(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray(),
            TtlTicks = config.Ttl?.Ticks,
            config.OrchestrationJson
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)))
            .ToLowerInvariant();
    }

    private Dictionary<string, string> BuildComposeRuntimeLabels(
        ComposeConfig config,
        string fingerprint)
    {
        var labels = DockerProvider.WithManagedLabels(config.Labels, config.OperationId);
        labels[DockerProvider.RequestFingerprintLabel] = fingerprint;
        labels[DockerProvider.ComposeResourceLabel] = bool.TrueString;
        labels[DockerProvider.ComposeProjectLabel] = config.ProjectName;
        if (config.Ttl is { } ttl && ttl > TimeSpan.Zero)
        {
            labels[DockerProvider.RuntimeExpiryLabel] = provider.UtcNow
                .Add(ttl)
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return labels;
    }

    public async Task<ComposeStatus> GetComposeStatusAsync(
        string projectName,
        Dictionary<string, string>? labels = null,
        CancellationToken cancellationToken = default)
    {
        var client = provider.CreateClient();
        var labelFilters = new Dictionary<string, bool>
        {
            [$"com.docker.compose.project={projectName}"] = true
        };

        if (labels is not null)
        {
            foreach (var (key, value) in labels)
                labelFilters[$"{key}={value}"] = true;
        }

        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = labelFilters
                }
            },
            cancellationToken);

        var services = containers.Select(container =>
        {
            container.Labels.TryGetValue("com.docker.compose.service", out var serviceName);
            container.Labels.TryGetValue("nodeId", out var nodeIdValue);
            Guid? nodeId = Guid.TryParse(nodeIdValue, out var parsedNodeId) ? parsedNodeId : null;
            var ports = container.Ports
                .Where(p => p.PublicPort > 0 && p.PrivatePort > 0)
                .GroupBy(p => (int)p.PrivatePort)
                .ToDictionary(g => g.Key, g => (int)g.First().PublicPort);

            return new ComposeServiceInstance(
                ServiceName: serviceName ?? string.Empty,
                ContainerId: container.ID,
                Status: container.State,
                NodeId: nodeId,
                PublishedPorts: ports,
                InternalHost: provider.PublishedHost,
                InternalPortMappings: ports);
        }).ToList();

        var status = services.Count == 0
            ? "not_found"
            : services.Any(s => string.Equals(s.Status, "running", StringComparison.OrdinalIgnoreCase))
                ? "running"
                : "stopped";

        return new ComposeStatus(projectName, status, services);
    }
}

internal sealed class BoundedDockerLogCapture
{
    internal const int MaxBytesPerStream = 32_768;
    private const int MaxReadBytes = MaxBytesPerStream * 2;
    private readonly MemoryStream _stdout = new(MaxBytesPerStream);
    private readonly MemoryStream _stderr = new(MaxBytesPerStream);
    private int _readBytes;

    internal int RemainingReadBytes => MaxReadBytes - _readBytes;
    internal int StdOutBytes => (int)_stdout.Length;
    internal int StdErrBytes => (int)_stderr.Length;

    internal void Append(MultiplexedStream.TargetStream target, ReadOnlySpan<byte> bytes)
    {
        var countedBytes = Math.Min(bytes.Length, RemainingReadBytes);
        _readBytes += countedBytes;
        var destination = target switch
        {
            MultiplexedStream.TargetStream.StandardOut => _stdout,
            MultiplexedStream.TargetStream.StandardError => _stderr,
            _ => null
        };
        if (destination is null)
            return;

        var writableBytes = Math.Min(countedBytes, MaxBytesPerStream - (int)destination.Length);
        if (writableBytes > 0)
            destination.Write(bytes[..writableBytes]);
    }

    internal (string? StdOut, string? StdErr) GetText()
        => (Decode(_stdout), Decode(_stderr));

    private static string? Decode(MemoryStream stream)
        => stream.Length == 0
            ? null
            : Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
}

internal sealed class ComposeProjectLock
{
    private readonly ConcurrentDictionary<string, Entry> entries =
        new(StringComparer.OrdinalIgnoreCase);

    internal int EntryCount => entries.Count;

    internal async ValueTask<IAsyncDisposable> AcquireAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = entries.GetOrAdd(projectName, static _ => new Entry());
            lock (entry.SyncRoot)
            {
                if (entry.Removed)
                    continue;
                entry.ReferenceCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Releaser(this, projectName, entry);
            }
            catch
            {
                ReleaseReference(projectName, entry, releaseSemaphore: false);
                throw;
            }
        }
    }

    private void Release(string projectName, Entry entry)
        => ReleaseReference(projectName, entry, releaseSemaphore: true);

    private void ReleaseReference(
        string projectName,
        Entry entry,
        bool releaseSemaphore)
    {
        if (releaseSemaphore)
            entry.Semaphore.Release();

        lock (entry.SyncRoot)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount != 0)
                return;

            entry.Removed = true;
            ((ICollection<KeyValuePair<string, Entry>>)entries).Remove(
                new KeyValuePair<string, Entry>(projectName, entry));
        }
    }

    private sealed class Entry
    {
        internal object SyncRoot { get; } = new();
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);
        internal int ReferenceCount { get; set; }
        internal bool Removed { get; set; }
    }

    private sealed class Releaser(
        ComposeProjectLock owner,
        string projectName,
        Entry entry) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                owner.Release(projectName, entry);
            return ValueTask.CompletedTask;
        }
    }
}
