using System.Text.Json;
using System.Diagnostics;
using CliWrap;
using CliWrap.Buffered;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runtime.Docker.Compose;

/// <summary>Owns Compose lifecycle as one adapter, keeping process invocation out of use cases.</summary>
public sealed class DockerComposeRuntime(
    DockerRuntimeOptions options,
    string dockerExecutable = "docker",
    string? workDirectory = null,
    TimeProvider? clock = null) : IComposeRuntime
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private const string RuntimeMetadataFileName = "noctf-runtime.json";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly string dockerExecutable = dockerExecutable;
    private readonly string workDirectory = workDirectory
        ?? Path.Combine(Path.GetTempPath(), "noctf-compose");

    public async Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken)
    {
        if ((request.PublishedPorts ?? []).Any(mapping =>
                mapping.HostPort != 0))
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Docker Compose public endpoints must request Docker-assigned host ports.");
        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForDocker(
            request,
            options.RuntimeLogMaxSizeBytes,
            options.RuntimeLogMaxFiles);
        Directory.CreateDirectory(workDirectory);
        var directory = Path.Combine(workDirectory, request.OperationId.ToString("N"));
        Directory.CreateDirectory(directory);
        await WriteRuntimeMetadataAsync(
            directory,
            new DockerComposeRuntimeMetadata(
                request.OperationId,
                request.ProjectName),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "compose.yaml"),
            prepared,
            cancellationToken);
        try
        {
            await RunDockerAsync(
                directory,
                request.ProjectName,
                "up",
                cancellationToken,
                "-d",
                "--pull",
                "missing",
                "--wait",
                "--wait-timeout",
                Math.Max(1, (int)Math.Ceiling(request.OperationTimeout.TotalSeconds)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            if (!await TryCleanUpFailedProvisionAsync(directory, request.ProjectName))
                throw new ComposeCleanupFailedException(
                    "Docker Compose provision failed and its partial resources could not be removed.",
                    exception);
            throw;
        }
        return new(
            request.OperationId,
            RuntimeProvider.Docker,
            request.ProjectName,
            directory,
            options.PublicHost,
            timeProvider.GetUtcNow());
    }

    public async Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken)
    {
        await DownAsync(
            receipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public async Task DownAsync(
        ComposeReceipt receipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        var directory = ResolveOwnedDirectory(receipt.Namespace);
        var warnings = new List<Exception>();
        var gracefulSucceeded = false;
        if (mode == RuntimeTerminationMode.GracefulThenForce
            && Directory.Exists(directory))
        {
            var started = Stopwatch.GetTimestamp();
            using var graceful = CreateStageToken(cancellationToken, policy.GracefulStopTimeout);
            try
            {
                await RunDockerAsync(
                    directory,
                    receipt.ProjectName,
                    "down",
                    graceful.Token,
                    "--timeout",
                    Math.Max(1, (int)Math.Ceiling(policy.GracefulStopTimeout.TotalSeconds))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--remove-orphans");
                gracefulSucceeded = !await ProjectResourcesRemainAsync(
                    receipt.ProjectName,
                    graceful.Token);
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "docker", "compose", "graceful",
                    gracefulSucceeded ? "success" : "warning",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "docker", "compose", "graceful", "timeout",
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
                    "docker", "compose", "graceful", "warning",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }

        if (mode == RuntimeTerminationMode.Force || !gracefulSucceeded)
        {
            NoCtfTelemetry.RecordRuntimeStopForce(
                "docker",
                mode == RuntimeTerminationMode.Force ? "requested" : "graceful_failed");
            var started = Stopwatch.GetTimestamp();
            using var force = CreateStageToken(cancellationToken, policy.ForceDeleteTimeout);
            if (Directory.Exists(directory))
            {
                try
                {
                    await RunDockerAsync(
                        directory,
                        receipt.ProjectName,
                        "down",
                        force.Token,
                        "--timeout",
                        "0",
                        "--remove-orphans");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    warnings.Add(exception);
                }
            }
            try
            {
                await ForceRemoveProjectContainersAsync(receipt.ProjectName, force.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                warnings.Add(exception);
            }
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "compose", "force_delete",
                warnings.Count == 0 ? "success" : "warning",
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }

        var networkStarted = Stopwatch.GetTimestamp();
        using (var network = CreateStageToken(
                   cancellationToken,
                   policy.NetworkCleanupTimeout))
        {
            try
            {
                await RemoveProjectNetworksAsync(receipt.ProjectName, network.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                warnings.Add(exception);
            }
        }
        NoCtfTelemetry.RecordRuntimeStopDuration(
            "docker", "compose", "network_cleanup",
            warnings.Count == 0 ? "success" : "warning",
            Stopwatch.GetElapsedTime(networkStarted).TotalSeconds);

        var verificationStarted = Stopwatch.GetTimestamp();
        using var verification = CreateStageToken(cancellationToken, policy.VerificationTimeout);
        try
        {
            await WaitUntilDestroyedAsync(receipt, verification.Token);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "compose", "verification", "success",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            NoCtfTelemetry.RecordRuntimeStopResourcesRemaining("docker", "compose");
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "compose", "verification", "timeout",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
            throw new InvalidOperationException(
                "Docker Compose resources remain after the termination budget.",
                warnings.Count == 0 ? null : new AggregateException(warnings));
        }
    }

    private async Task WaitUntilDestroyedAsync(
        ComposeReceipt receipt,
        CancellationToken cancellationToken)
    {
        var delays = new[] { 200, 400, 800, 1_000 };
        var attempt = 0;
        while (true)
        {
            if (!await ProjectResourcesRemainAsync(
                    receipt.ProjectName,
                    cancellationToken))
                return;
            await Task.Delay(
                TimeSpan.FromMilliseconds(delays[Math.Min(attempt++, delays.Length - 1)]),
                timeProvider,
                cancellationToken);
        }
    }

    private async Task<bool> ProjectResourcesRemainAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var containers = await ListProjectResourceIdsAsync(
            ["ps", "--all", "--quiet"],
            projectName,
            cancellationToken);
        var networks = await ListProjectResourceIdsAsync(
            ["network", "ls", "--quiet"],
            projectName,
            cancellationToken);
        return containers.Length > 0 || networks.Length > 0;
    }

    private async Task ForceRemoveProjectContainersAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var containers = await ListProjectResourceIdsAsync(
            ["ps", "--all", "--quiet"],
            projectName,
            cancellationToken);
        foreach (var containerId in containers)
        {
            _ = await RunRawDockerAsync(
                cancellationToken,
                "rm",
                "--force",
                containerId);
        }
    }

    private async Task RemoveProjectNetworksAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var networks = await ListProjectResourceIdsAsync(
            ["network", "ls", "--quiet"],
            projectName,
            cancellationToken);
        foreach (var networkId in networks)
        {
            _ = await RunRawDockerAsync(
                cancellationToken,
                "network",
                "rm",
                networkId);
        }
    }

    private async Task<string[]> ListProjectResourceIdsAsync(
        IReadOnlyList<string> command,
        string projectName,
        CancellationToken cancellationToken)
    {
        var output = await RunRawDockerAsync(
            cancellationToken,
            [.. command, "--filter", $"label=com.docker.compose.project={projectName}"]);
        return output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<string> RunRawDockerAsync(
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await Cli.Wrap(dockerExecutable)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
        if (result.ExitCode != 0)
            throw new ComposeCommandFailedException(result.ExitCode, result.StandardError);
        return result.StandardOutput;
    }

    private static CancellationTokenSource CreateStageToken(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    public async Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken)
    {
        var directory = ResolveOwnedDirectory(receipt.Namespace);
        if (!Directory.Exists(directory))
            return new(receipt.ProjectName, RuntimeStatus.Stopped, []);
        var result = await RunDockerAsync(
            directory,
            receipt.ProjectName,
            "ps",
            cancellationToken,
            "--format",
            "json");
        var entries = ParseProcesses(result);
        var services = entries
            .Select(entry => new ComposeServiceStatus(
                entry.Service,
                entry.Id,
                ToRuntimeStatus(entry.State),
                (entry.Publishers ?? [])
                    .Where(publisher => publisher.TargetPort is > 0
                        && publisher.PublishedPort is > 0)
                    .Select(publisher => new
                        MappedPublishedPort(publisher.TargetPort, publisher.PublishedPort))
                    .GroupBy(publisher => publisher.TargetPort)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().PublishedPort),
                entry.Service))
            .ToArray();
        var resourceStatuses = entries
            .Select(entry => ToRuntimeStatus(entry.State))
            .ToArray();
        var status = services.Length == 0
            ? RuntimeStatus.Stopped
            : resourceStatuses.All(resourceStatus =>
                    resourceStatus == RuntimeStatus.Running)
                ? RuntimeStatus.Running
                : resourceStatuses.Any(resourceStatus =>
                    resourceStatus == RuntimeStatus.Failed)
                    ? RuntimeStatus.Failed
                    : RuntimeStatus.Starting;
        return new(receipt.ProjectName, status, services);
    }

    public async Task<ContainerExecResult> ExecAsync(
        ComposeReceipt receipt,
        string serviceName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            string[] arguments = ["-T", serviceName, .. command];
            await RunDockerAsync(
                receipt.Namespace,
                receipt.ProjectName,
                "exec",
                timeoutSource.Token,
                arguments);
            return new(0, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(-1, true);
        }
        catch (ComposeCommandFailedException exception)
        {
            return new(exception.ExitCode, false);
        }
    }

    private async Task<string> RunDockerAsync(
        string directory,
        string project,
        string command,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        string[] commandArguments =
        [
            "compose",
            "-p",
            project,
            "-f",
            Path.Combine(directory, "compose.yaml"),
            command,
            .. arguments
        ];
        var result = await Cli.Wrap(dockerExecutable)
            .WithWorkingDirectory(directory)
            .WithArguments(commandArguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
        if (result.ExitCode != 0)
            throw new ComposeCommandFailedException(
                result.ExitCode,
                result.StandardError);
        return result.StandardOutput;
    }

    private string ResolveOwnedDirectory(string directory)
    {
        var root = Path.GetFullPath(workDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(root, comparison))
            throw new InvalidOperationException("Compose receipt does not belong to this Runner.");
        return resolved;
    }

    private async Task<bool> TryCleanUpFailedProvisionAsync(string directory, string project)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await RunDockerAsync(
                directory,
                project,
                "down",
                cleanup.Token,
                "--remove-orphans");
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return true;
        }
        catch
        {
            // Preserve the Compose definition for operator cleanup when Docker cleanup fails.
            return false;
        }
    }

    public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workDirectory))
            return [];
        var identities = new List<RuntimeResourceIdentity>();
        foreach (var directory in Directory.EnumerateDirectories(workDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await TryReadRuntimeMetadataAsync(directory, cancellationToken);
            if (metadata is not null)
                identities.Add(new(metadata.OperationId));
        }
        return identities.Distinct().ToArray();
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        await DestroyByIdentityAsync(
            identity,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        if (identity.RuntimeInstanceId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(identity));
        if (!Directory.Exists(workDirectory))
            return;
        foreach (var directory in Directory.EnumerateDirectories(workDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await TryReadRuntimeMetadataAsync(directory, cancellationToken);
            if (metadata is null
                || metadata.OperationId != identity.RuntimeInstanceId)
                continue;
            var composePath = Path.Combine(directory, "compose.yaml");
            if (File.Exists(composePath))
            {
                await DownAsync(
                    new ComposeReceipt(
                        metadata.OperationId,
                        RuntimeProvider.Docker,
                        metadata.ProjectName,
                        directory,
                        options.PublicHost,
                        default),
                    mode,
                    policy,
                    cancellationToken);
            }
            else if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static RuntimeStatus ToRuntimeStatus(string status) => status.ToLowerInvariant() switch
    {
        "running" => RuntimeStatus.Running,
        "created" => RuntimeStatus.Pending,
        "restarting" => RuntimeStatus.Starting,
        "exited" or "dead" => RuntimeStatus.Stopped,
        _ => RuntimeStatus.Failed
    };

    private static IReadOnlyList<DockerComposeProcess> ParseProcesses(string output)
    {
        var value = output.Trim();
        if (value.Length == 0)
            return [];
        if (value[0] == '[')
            return JsonSerializer.Deserialize<IReadOnlyList<DockerComposeProcess>>(
                value,
                JsonOptions) ?? [];
        return value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<DockerComposeProcess>(line, JsonOptions)
                ?? throw new InvalidOperationException(
                    "Docker Compose returned an empty process entry."))
            .ToArray();
    }

    private static async Task WriteRuntimeMetadataAsync(
        string directory,
        DockerComposeRuntimeMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, RuntimeMetadataFileName);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static async Task<DockerComposeRuntimeMetadata?> TryReadRuntimeMetadataAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, RuntimeMetadataFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var metadata = JsonSerializer.Deserialize<DockerComposeRuntimeMetadata>(
                json,
                JsonOptions);
            return metadata is { OperationId: var operationId }
                && operationId != Guid.Empty
                && !string.IsNullOrWhiteSpace(metadata.ProjectName)
                    ? metadata
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record DockerComposeProcess(
        string Id,
        string Service,
        string State,
        IReadOnlyList<DockerComposePublisher>? Publishers);

    private sealed record DockerComposePublisher(int TargetPort, int PublishedPort);
    private sealed record MappedPublishedPort(int TargetPort, int PublishedPort);
    private sealed record DockerComposeRuntimeMetadata(
        Guid OperationId,
        string ProjectName);
}

public sealed class ComposeCommandFailedException(int exitCode, string message)
    : InvalidOperationException(message)
{
    public int ExitCode { get; } = exitCode;
}

public sealed class ComposeCleanupFailedException(string message, Exception innerException)
    : Exception(message, innerException);
