using System.Diagnostics;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Docker.Compose;

/// <summary>Owns Compose lifecycle as one adapter, keeping process invocation out of use cases.</summary>
public sealed class DockerComposeRuntime : IComposeRuntime
{
    private readonly string dockerExecutable;
    private readonly string workDirectory;

    public DockerComposeRuntime(string dockerExecutable = "docker", string? workDirectory = null)
    {
        this.dockerExecutable = dockerExecutable;
        this.workDirectory = workDirectory ?? Path.Combine(Path.GetTempPath(), "noctf-compose");
        Directory.CreateDirectory(this.workDirectory);
    }

    public async Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(workDirectory, request.OperationId.ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "compose.yaml"), request.ComposeYaml, cancellationToken);
        await RunDockerAsync(directory, request.ProjectName, "up", cancellationToken, "-d");
        return new(request.OperationId, RuntimeProvider.Docker, request.ProjectName, directory, DateTimeOffset.UtcNow);
    }

    public Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken) =>
        RunDockerAsync(receipt.Namespace, receipt.ProjectName, "down", cancellationToken, "--remove-orphans");

    public async Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken)
    {
        var result = await RunDockerAsync(receipt.Namespace, receipt.ProjectName, "ps", cancellationToken, "--format", "{{.Service}}|{{.State}}");
        var services = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new ComposeServiceStatus(parts[0], parts[0], ToRuntimeStatus(parts[1]), new Dictionary<int, int>(), null))
            .ToList();
        return new(receipt.ProjectName, services.Count == 0 ? RuntimeStatus.Stopped : RuntimeStatus.Running, services);
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
        await process.WaitForExitAsync(cancellationToken);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(await process.StandardError.ReadToEndAsync(cancellationToken));
        return output;
    }

    private static RuntimeStatus ToRuntimeStatus(string status) => status.ToLowerInvariant() switch
    {
        "running" => RuntimeStatus.Running,
        "created" => RuntimeStatus.Pending,
        "restarting" => RuntimeStatus.Starting,
        "exited" or "dead" => RuntimeStatus.Stopped,
        _ => RuntimeStatus.Failed
    };
}
