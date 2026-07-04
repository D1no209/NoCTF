using System.Diagnostics;
using System.Text;

namespace NoCTF.Container.Docker;

public static class DockerComposeRunner
{
    public static async Task<string> RunAsync(
        string composeYaml,
        string projectName,
        IReadOnlyList<string> args,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        var composeFile = Path.Combine(Path.GetTempPath(), $"noctf-compose-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(composeFile, composeYaml, cancellationToken);

        try
        {
            var output = await RunDockerAsync(
                BuildArguments(composeFile, projectName, args),
                environmentVariables,
                cancellationToken);

            return output;
        }
        finally
        {
            if (File.Exists(composeFile))
                File.Delete(composeFile);
        }
    }

    public static void ValidateComposeYaml(string composeYaml)
    {
        if (string.IsNullOrWhiteSpace(composeYaml))
            throw new InvalidOperationException("Compose YAML cannot be empty.");

        var forbiddenTokens = new[]
        {
            "privileged:",
            "network_mode: host",
            "pid: host",
            "ipc: host",
            "cgroup_parent:",
            "devices:",
            "cap_add:",
            "extra_hosts:",
            "/var/run/docker.sock",
        };

        foreach (var token in forbiddenTokens)
        {
            if (composeYaml.Contains(token, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Compose YAML contains forbidden directive: {token}");
        }

        var forbiddenSecurityOptions = new[]
        {
            "no-new-privileges:false",
            "seccomp=unconfined",
            "apparmor=unconfined",
            "label:disable",
        };

        foreach (var token in forbiddenSecurityOptions)
        {
            if (composeYaml.Contains(token, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Compose YAML contains forbidden security option: {token}");
        }
    }

    private static string BuildArguments(string composeFile, string projectName, IReadOnlyList<string> args)
    {
        var builder = new StringBuilder();
        builder.Append("compose -f ");
        AppendQuoted(builder, composeFile);
        builder.Append(" -p ");
        AppendQuoted(builder, projectName);

        foreach (var arg in args)
        {
            builder.Append(' ');
            AppendQuoted(builder, arg);
        }

        return builder.ToString();
    }

    private static async Task<string> RunDockerAsync(
        string arguments,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
                startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker compose process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker {arguments} failed with exit code {process.ExitCode}: {stderr}");

        return stdout;
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        builder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        builder.Append('"');
    }
}
