using System.Diagnostics;
using System.Text;
using YamlDotNet.RepresentationModel;

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

        if (composeYaml.Contains("/var/run/docker.sock", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Compose YAML contains forbidden Docker socket mount.");

        var yaml = new YamlStream();
        try
        {
            using var reader = new StringReader(composeYaml);
            yaml.Load(reader);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Compose YAML is invalid.", ex);
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Compose YAML must be a mapping document.");

        if (TryGetMapping(root, "services") is not { } services || services.Children.Count == 0)
            throw new InvalidOperationException("Compose YAML must define at least one service.");

        foreach (var (_, value) in services.Children)
        {
            if (value is not YamlMappingNode service)
                throw new InvalidOperationException("Compose service definitions must be mappings.");

            ValidateComposeService(service);
        }
    }

    private static void ValidateComposeService(YamlMappingNode service)
    {
        foreach (var (keyNode, valueNode) in service.Children)
        {
            var key = ScalarValue(keyNode).ToLowerInvariant();
            switch (key)
            {
                case "privileged":
                case "network_mode":
                case "cgroup_parent":
                case "devices":
                case "cap_add":
                case "extra_hosts":
                    throw new InvalidOperationException($"Compose YAML contains forbidden service directive: {key}");
                case "pid":
                case "ipc":
                    if (ScalarValue(valueNode).Equals("host", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Compose YAML contains forbidden host namespace directive: {key}");
                    break;
                case "security_opt":
                    ValidateSecurityOptions(valueNode);
                    break;
                case "volumes":
                    ValidateVolumes(valueNode);
                    break;
            }
        }
    }

    private static void ValidateSecurityOptions(YamlNode node)
    {
        foreach (var option in ToScalarList(node))
        {
            var normalized = option.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            if (normalized.Equals("no-new-privileges:true", StringComparison.OrdinalIgnoreCase))
                continue;

            throw new InvalidOperationException($"Compose YAML contains forbidden security option: {option}");
        }
    }

    private static void ValidateVolumes(YamlNode node)
    {
        foreach (var item in SequenceItems(node))
        {
            if (item is YamlMappingNode mapping)
            {
                var type = ScalarValue(GetValue(mapping, "type"));
                if (type.Equals("bind", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Compose YAML bind mounts are not allowed.");

                var source = ScalarValue(GetValue(mapping, "source"));
                if (IsUnsafeVolumeSource(source))
                    throw new InvalidOperationException("Compose YAML host path mounts are not allowed.");
            }
            else
            {
                var volume = ScalarValue(item);
                if (IsUnsafeVolumeSource(ParseVolumeSource(volume)))
                    throw new InvalidOperationException("Compose YAML host path mounts are not allowed.");
            }
        }
    }

    private static bool IsUnsafeVolumeSource(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ||
               trimmed.StartsWith(".", StringComparison.Ordinal) ||
               trimmed.StartsWith("~", StringComparison.Ordinal) ||
               trimmed.StartsWith("\\", StringComparison.Ordinal) ||
               trimmed.Contains("/var/run/docker.sock", StringComparison.OrdinalIgnoreCase) ||
               (trimmed.Length >= 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' &&
                (trimmed[2] == '\\' || trimmed[2] == '/'));
    }

    private static string ParseVolumeSource(string volume)
    {
        var trimmed = volume.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !trimmed.Contains(':', StringComparison.Ordinal))
            return trimmed;

        if (trimmed.Length >= 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' &&
            (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            var nextColon = trimmed.IndexOf(':', 2);
            return nextColon < 0 ? trimmed : trimmed[..nextColon];
        }

        return trimmed[..trimmed.IndexOf(':', StringComparison.Ordinal)];
    }

    private static IEnumerable<YamlNode> SequenceItems(YamlNode node)
        => node is YamlSequenceNode sequence
            ? sequence.Children
            : throw new InvalidOperationException("Compose YAML directive must be a list.");

    private static IEnumerable<string> ToScalarList(YamlNode node)
        => SequenceItems(node).Select(ScalarValue);

    private static YamlMappingNode? TryGetMapping(YamlMappingNode mapping, string key)
        => GetValue(mapping, key) as YamlMappingNode;

    private static YamlNode? GetValue(YamlMappingNode mapping, string key)
        => mapping.Children.FirstOrDefault(kvp =>
            ScalarValue(kvp.Key).Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string ScalarValue(YamlNode? node)
        => node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty;

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
