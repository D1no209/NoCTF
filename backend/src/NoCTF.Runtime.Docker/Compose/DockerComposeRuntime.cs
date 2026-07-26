using System.Diagnostics;
using System.Text.Json;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runtime.Docker.Compose;

/// <summary>Owns Compose lifecycle as one adapter, keeping process invocation out of use cases.</summary>
public sealed class DockerComposeRuntime(
    DockerRuntimeOptions options,
    string dockerExecutable = "docker",
    string? workDirectory = null) : IComposeRuntime
{
    private const string IngressMetadataFileName = "noctf-ingress.json";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly string dockerExecutable = dockerExecutable;
    private readonly string workDirectory = workDirectory
        ?? Path.Combine(Path.GetTempPath(), "noctf-compose");

    public async Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDirectory);
        var directory = Path.Combine(workDirectory, request.OperationId.ToString("N"));
        Directory.CreateDirectory(directory);
        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForDocker(request);
        var ingress = DockerComposeIngressProxyPolicy.Apply(prepared, request, options);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "compose.yaml"),
            ingress.ComposeYaml,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(directory, IngressMetadataFileName),
            JsonSerializer.Serialize(
                new DockerComposeIngressMetadata(
                    ingress.ProxyServiceName,
                    ingress.Bindings),
                JsonOptions),
            cancellationToken);
        try
        {
            await RunDockerAsync(
                directory,
                request.ProjectName,
                "up",
                cancellationToken,
                "-d",
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
            request.Generation,
            DateTimeOffset.UtcNow);
    }

    public async Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken)
    {
        var directory = ResolveOwnedDirectory(receipt.Namespace);
        await RunDockerAsync(
            directory,
            receipt.ProjectName,
            "down",
            cancellationToken,
            "--remove-orphans");
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    public async Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken)
    {
        var directory = ResolveOwnedDirectory(receipt.Namespace);
        var result = await RunDockerAsync(
            directory,
            receipt.ProjectName,
            "ps",
            cancellationToken,
            "--format",
            "json");
        var entries = ParseProcesses(result);
        var ingress = await ReadIngressMetadataAsync(directory, cancellationToken);
        var proxy = ingress.ProxyServiceName is null
            ? null
            : entries.SingleOrDefault(entry =>
                string.Equals(
                    entry.Service,
                    ingress.ProxyServiceName,
                    StringComparison.Ordinal));
        var services = entries
            .Where(entry => !string.Equals(
                entry.Service,
                ingress.ProxyServiceName,
                StringComparison.Ordinal))
            .Select(entry => new ComposeServiceStatus(
                entry.Service,
                entry.Id,
                ToRuntimeStatus(entry.State),
                (entry.Publishers ?? [])
                    .Where(publisher => publisher.TargetPort is > 0
                        && publisher.PublishedPort is > 0)
                    .Select(publisher => new
                        MappedPublishedPort(publisher.TargetPort, publisher.PublishedPort))
                    .Concat(PublishedIngressPorts(ingress, proxy, entry.Service))
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
            : ingress.ProxyServiceName is not null && proxy is null
                ? RuntimeStatus.Failed
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
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dockerExecutable,
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("compose");
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add(project);
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add(Path.Combine(directory, "compose.yaml"));
        process.StartInfo.ArgumentList.Add(command);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
            throw new ComposeCommandFailedException(
                process.ExitCode,
                error);
        return output;
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

    private async Task<DockerComposeIngressMetadata> ReadIngressMetadataAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, IngressMetadataFileName);
        if (!File.Exists(path))
            return new(null, []);
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DockerComposeIngressMetadata>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                "Docker Compose ingress metadata is invalid.");
    }

    private static IEnumerable<MappedPublishedPort> PublishedIngressPorts(
        DockerComposeIngressMetadata ingress,
        DockerComposeProcess? proxy,
        string targetService)
    {
        if (proxy?.Publishers is null)
            return [];
        var publishedByListener = proxy.Publishers
            .Where(publisher => publisher.TargetPort is > 0
                && publisher.PublishedPort is > 0)
            .GroupBy(publisher => publisher.TargetPort)
            .ToDictionary(
                group => group.Key,
                group => group.First().PublishedPort);
        return ingress.Bindings
            .Where(binding => string.Equals(
                binding.TargetService,
                targetService,
                StringComparison.Ordinal))
            .Where(binding => publishedByListener.ContainsKey(binding.ListenerPort))
            .Select(binding => new MappedPublishedPort(
                binding.TargetPort,
                publishedByListener[binding.ListenerPort]));
    }

    private sealed record DockerComposeProcess(
        string Id,
        string Service,
        string State,
        IReadOnlyList<DockerComposePublisher>? Publishers);

    private sealed record DockerComposePublisher(int TargetPort, int PublishedPort);
    private sealed record MappedPublishedPort(int TargetPort, int PublishedPort);
    private sealed record DockerComposeIngressMetadata(
        string? ProxyServiceName,
        IReadOnlyList<DockerComposeIngressBinding> Bindings);
}

public sealed class ComposeCommandFailedException(int exitCode, string message)
    : InvalidOperationException(message)
{
    public int ExitCode { get; } = exitCode;
}

public sealed class ComposeCleanupFailedException(string message, Exception innerException)
    : Exception(message, innerException);
