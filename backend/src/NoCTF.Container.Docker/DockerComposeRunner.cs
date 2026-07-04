using System.Diagnostics;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Container.Docker;

public static class DockerComposeRunner
{
    private static readonly HashSet<string> AllowedRootKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "version",
        "services",
        "volumes",
        "networks"
    };

    private static readonly HashSet<string> AllowedServiceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "command",
        "entrypoint",
        "environment",
        "ports",
        "labels",
        "depends_on",
        "restart",
        "security_opt",
        "cap_drop",
        "read_only",
        "user",
        "deploy",
        "volumes",
        "healthcheck"
    };

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

        ValidateRoot(root);

        if (TryGetMapping(root, "services") is not { } services || services.Children.Count == 0)
            throw new InvalidOperationException("Compose YAML must define at least one service.");

        foreach (var (_, value) in services.Children)
        {
            if (value is not YamlMappingNode service)
                throw new InvalidOperationException("Compose service definitions must be mappings.");

            ValidateComposeService(service);
        }

        ValidateTopLevelVolumes(TryGetMapping(root, "volumes"));
        ValidateTopLevelNetworks(TryGetMapping(root, "networks"));
    }

    private static void ValidateRoot(YamlMappingNode root)
    {
        foreach (var key in root.Children.Keys.Select(ScalarValue))
        {
            if (!AllowedRootKeys.Contains(key))
                throw new InvalidOperationException($"Compose YAML contains unsupported top-level directive: {key}");
        }
    }

    private static void ValidateComposeService(YamlMappingNode service)
    {
        foreach (var (keyNode, valueNode) in service.Children)
        {
            var key = ScalarValue(keyNode).ToLowerInvariant();
            if (!AllowedServiceKeys.Contains(key))
                throw new InvalidOperationException($"Compose YAML contains unsupported service directive: {key}");

            switch (key)
            {
                case "privileged":
                case "network_mode":
                case "cgroup_parent":
                case "devices":
                case "cap_add":
                case "extra_hosts":
                case "build":
                case "env_file":
                case "secrets":
                case "configs":
                case "networks":
                case "extends":
                case "links":
                case "container_name":
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
                case "deploy":
                    ValidateDeploy(valueNode);
                    break;
                case "healthcheck":
                    ValidateHealthcheck(valueNode);
                    break;
            }
        }
    }

    private static void ValidateDeploy(YamlNode node)
    {
        if (node is not YamlMappingNode deploy)
            throw new InvalidOperationException("Compose deploy directive must be a mapping.");

        foreach (var (keyNode, valueNode) in deploy.Children)
        {
            var key = ScalarValue(keyNode);
            if (!key.Equals("resources", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Compose deploy directive is not supported: {key}");

            if (valueNode is not YamlMappingNode resources)
                throw new InvalidOperationException("Compose deploy.resources directive must be a mapping.");

            foreach (var resourceKey in resources.Children.Keys.Select(ScalarValue))
            {
                if (!resourceKey.Equals("limits", StringComparison.OrdinalIgnoreCase) &&
                    !resourceKey.Equals("reservations", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Compose deploy.resources directive is not supported: {resourceKey}");
            }
        }
    }

    private static void ValidateHealthcheck(YamlNode node)
    {
        if (node is not YamlMappingNode healthcheck)
            throw new InvalidOperationException("Compose healthcheck directive must be a mapping.");

        foreach (var key in healthcheck.Children.Keys.Select(ScalarValue))
        {
            if (!key.Equals("test", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("interval", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("timeout", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("retries", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("start_period", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("disable", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Compose healthcheck directive is not supported: {key}");
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

    private static void ValidateTopLevelVolumes(YamlMappingNode? volumes)
    {
        if (volumes is null)
            return;

        foreach (var (_, valueNode) in volumes.Children)
        {
            if (valueNode is null or YamlScalarNode)
                continue;

            if (valueNode is not YamlMappingNode volume)
                throw new InvalidOperationException("Compose top-level volumes must be mappings.");

            foreach (var (keyNode, childValue) in volume.Children)
            {
                var key = ScalarValue(keyNode);
                if (key.Equals("driver_opts", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("external", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Compose top-level volume directive is forbidden: {key}");

                if (key.Equals("driver", StringComparison.OrdinalIgnoreCase))
                {
                    var driver = ScalarValue(childValue);
                    if (!string.IsNullOrWhiteSpace(driver) && !driver.Equals("local", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Compose top-level volumes may only use the local driver.");
                    continue;
                }

                if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Compose top-level volume names are controlled by NoCTF.");

                if (!key.Equals("labels", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Compose top-level volume directive is not supported: {key}");
            }
        }
    }

    private static void ValidateTopLevelNetworks(YamlMappingNode? networks)
    {
        if (networks is null)
            return;

        foreach (var (_, valueNode) in networks.Children)
        {
            if (valueNode is null or YamlScalarNode)
                continue;

            if (valueNode is not YamlMappingNode network)
                throw new InvalidOperationException("Compose top-level networks must be mappings.");

            foreach (var key in network.Children.Keys.Select(ScalarValue))
            {
                if (key.Equals("external", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("driver_opts", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("ipam", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Compose top-level network directive is forbidden: {key}");

                if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Compose top-level network names are controlled by NoCTF.");

                if (!key.Equals("driver", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("labels", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Compose top-level network directive is not supported: {key}");
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
