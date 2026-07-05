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
        "pids_limit",
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

        if (composeYaml.Contains("${", StringComparison.Ordinal))
            throw new InvalidOperationException("Compose YAML variable interpolation is not allowed.");

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
                case "ports":
                    ValidatePorts(valueNode);
                    break;
                case "volumes":
                    ValidateVolumes(valueNode);
                    break;
                case "deploy":
                    ValidateDeploy(valueNode);
                    break;
                case "pids_limit":
                    ValidatePidsLimit(valueNode);
                    break;
                case "healthcheck":
                    ValidateHealthcheck(valueNode);
                    break;
            }
        }

        if (!HasNoNewPrivileges(service))
            throw new InvalidOperationException("Compose services must set security_opt: no-new-privileges:true.");
        if (!HasCapDropAll(service))
            throw new InvalidOperationException("Compose services must drop all Linux capabilities.");
        if (!HasNonRootUser(service))
            throw new InvalidOperationException("Compose services must run as a non-root user.");
        if (!HasReadOnlyRootFilesystem(service))
            throw new InvalidOperationException("Compose services must set read_only: true.");
        if (!HasRequiredResourceLimits(service))
            throw new InvalidOperationException("Compose services must define deploy.resources.limits.cpus and memory.");
        if (!HasValidPidsLimit(service))
            throw new InvalidOperationException("Compose services must define pids_limit between 1 and 512.");
    }

    private static void ValidatePorts(YamlNode node)
    {
        foreach (var item in SequenceItems(node))
        {
            if (item is YamlMappingNode mapping)
            {
                var hostIp = ScalarValue(GetValue(mapping, "host_ip"));
                if (!string.IsNullOrWhiteSpace(hostIp))
                    throw new InvalidOperationException("Compose port host_ip bindings are not allowed.");

                var mode = ScalarValue(GetValue(mapping, "mode"));
                if (mode.Equals("host", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Compose host-mode ports are not allowed.");

                var published = ScalarValue(GetValue(mapping, "published"));
                if (!string.IsNullOrWhiteSpace(published) && published != "0")
                    throw new InvalidOperationException("Compose fixed published ports are not allowed.");

                continue;
            }

            var value = ScalarValue(item).Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (value.Contains('[', StringComparison.Ordinal) ||
                value.Count(c => c == ':') > 1)
                throw new InvalidOperationException("Compose port host bindings are not allowed.");

            var segments = value.Split(':', StringSplitOptions.TrimEntries);
            if (segments.Length > 1 && segments[0] != "0")
                throw new InvalidOperationException("Compose fixed published ports are not allowed.");
        }
    }

    private static bool HasNoNewPrivileges(YamlMappingNode service)
        => GetValue(service, "security_opt") is { } node &&
           ToScalarList(node).Any(option =>
               option.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Equals("no-new-privileges:true", StringComparison.OrdinalIgnoreCase));

    private static bool HasCapDropAll(YamlMappingNode service)
        => GetValue(service, "cap_drop") is { } node &&
           ToScalarList(node).Any(option => option.Equals("ALL", StringComparison.OrdinalIgnoreCase));

    private static bool HasNonRootUser(YamlMappingNode service)
    {
        var user = ScalarValue(GetValue(service, "user")).Trim();
        if (string.IsNullOrWhiteSpace(user))
            return false;

        var first = user.Split(':', 2, StringSplitOptions.TrimEntries)[0];
        return !first.Equals("0", StringComparison.Ordinal) &&
               !first.Equals("root", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReadOnlyRootFilesystem(YamlMappingNode service)
        => ScalarValue(GetValue(service, "read_only"))
            .Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool HasRequiredResourceLimits(YamlMappingNode service)
    {
        if (GetValue(service, "deploy") is not YamlMappingNode deploy ||
            GetValue(deploy, "resources") is not YamlMappingNode resources ||
            GetValue(resources, "limits") is not YamlMappingNode limits)
        {
            return false;
        }

        return IsValidCpuLimit(ScalarValue(GetValue(limits, "cpus"))) &&
               IsValidMemoryLimit(ScalarValue(GetValue(limits, "memory")));
    }

    private static bool HasValidPidsLimit(YamlMappingNode service)
        => int.TryParse(ScalarValue(GetValue(service, "pids_limit")), out var value) &&
           value is > 0 and <= 512;

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

    private static void ValidatePidsLimit(YamlNode node)
    {
        if (!int.TryParse(ScalarValue(node), out var value) || value is <= 0 or > 512)
            throw new InvalidOperationException("Compose pids_limit must be between 1 and 512.");
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

                if (key.Equals("driver", StringComparison.OrdinalIgnoreCase))
                {
                    var driver = ScalarValue(GetValue(network, key));
                    if (!string.IsNullOrWhiteSpace(driver) && !driver.Equals("bridge", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Compose top-level networks may only use the bridge driver.");
                    continue;
                }

                if (!key.Equals("labels", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Compose top-level network directive is not supported: {key}");
            }
        }
    }

    private static bool IsValidCpuLimit(string value)
        => double.TryParse(
               value,
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture,
               out var cpus) &&
           cpus is > 0 and <= 2;

    private static bool IsValidMemoryLimit(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"^(?<number>\d+(?:\.\d+)?)(?<unit>b|k|kb|m|mb|g|gb|ki|kib|mi|mib|gi|gib)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success ||
            !double.TryParse(
                match.Groups["number"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number) ||
            number <= 0)
        {
            return false;
        }

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var multiplier = unit switch
        {
            "" or "b" => 1D,
            "k" or "kb" => 1000D,
            "ki" or "kib" => 1024D,
            "m" or "mb" => 1000D * 1000D,
            "mi" or "mib" => 1024D * 1024D,
            "g" or "gb" => 1000D * 1000D * 1000D,
            "gi" or "gib" => 1024D * 1024D * 1024D,
            _ => 0D
        };

        var bytes = number * multiplier;
        return bytes is > 0 and <= 2D * 1024D * 1024D * 1024D;
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

        var inheritedEnvironment = new Dictionary<string, string?>(startInfo.Environment, StringComparer.OrdinalIgnoreCase);
        startInfo.Environment.Clear();
        foreach (var key in new[]
                 {
                     "PATH", "Path", "SystemRoot", "WINDIR", "DOCKER_HOST", "DOCKER_CONFIG",
                     "HOME", "USERPROFILE", "TMP", "TEMP", "TMPDIR"
                 })
        {
            if (inheritedEnvironment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                startInfo.Environment[key] = value;
        }
        startInfo.Environment["COMPOSE_DISABLE_ENV_FILE"] = "1";

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
                startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker compose process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker {arguments} failed with exit code {process.ExitCode}: {stderr}");

        return stdout;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cancellation cleanup must not hide the original cancellation.
        }
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        builder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        builder.Append('"');
    }
}
