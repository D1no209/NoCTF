using System.Diagnostics;

namespace NoCTF.Runtime.Kubernetes.Compose;

public interface IKomposeConverter
{
    Task<string> ConvertAsync(
        string composeYaml,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class KomposeConverter(
    string executable = "/usr/local/bin/kompose",
    string requiredVersion = "v1.38.0",
    string? workDirectory = null) : IKomposeConverter
{
    private readonly string workDirectory = workDirectory
        ?? Path.Combine(Path.GetTempPath(), "noctf-kompose");

    public async Task<string> ConvertAsync(
        string composeYaml,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            Directory.CreateDirectory(workDirectory);
            var version = await RunAsync(null, deadline.Token, "version");
            if (!ContainsVersion(version.Output, version.Error, requiredVersion))
                throw new InvalidOperationException(
                    $"Kompose version must be exactly '{requiredVersion}'.");

            var directory = Path.Combine(workDirectory, Guid.NewGuid().ToString("N"));
            var outputDirectory = Path.Combine(directory, "manifests");
            Directory.CreateDirectory(outputDirectory);
            try
            {
                var composePath = Path.Combine(directory, "compose.yaml");
                await File.WriteAllTextAsync(composePath, composeYaml, deadline.Token);
                _ = await RunAsync(
                    directory,
                    deadline.Token,
                    "--provider",
                    "kubernetes",
                    "--file",
                    composePath,
                    "convert",
                    "--controller",
                    "deployment",
                    "--out",
                    outputDirectory);
                var manifestFiles = Directory
                    .EnumerateFiles(outputDirectory, "*.yaml", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(
                        outputDirectory,
                        "*.yml",
                        SearchOption.AllDirectories))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (manifestFiles.Length == 0)
                    throw new InvalidOperationException(
                        "Kompose did not produce Kubernetes manifests.");
                var manifests = new List<string>(manifestFiles.Length);
                foreach (var manifestFile in manifestFiles)
                    manifests.Add(await File.ReadAllTextAsync(manifestFile, deadline.Token));
                return string.Join("\n---\n", manifests);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Kompose conversion exceeded its operation timeout.");
        }
    }

    private async Task<CommandResult> RunAsync(
        string? directory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directory ?? workDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
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

        var result = new CommandResult(await output, await error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Kompose exited with code {process.ExitCode}: {result.Error}");
        return result;
    }

    private static bool ContainsVersion(string output, string error, string requiredVersion)
    {
        if (!Version.TryParse(requiredVersion.TrimStart('v', 'V'), out var required))
            throw new InvalidOperationException(
                $"Configured Kompose version '{requiredVersion}' is invalid.");
        return string.Concat(output, " ", error)
            .Split(
                [' ', '\t', '\r', '\n', '(', ')', ','],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.TrimStart('v', 'V'))
            .Any(token => Version.TryParse(token, out var actual) && actual == required);
    }

    private sealed record CommandResult(string Output, string Error);
}
