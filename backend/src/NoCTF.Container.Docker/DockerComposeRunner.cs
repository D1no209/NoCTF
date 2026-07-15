using System.Diagnostics;
using System.Globalization;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Container.Docker;

public static class DockerComposeRunner
{
    internal const int MaxCapturedBytesPerStream = 1_048_576;
    internal const int MaxFailureDiagnosticCharacters = 4_096;
    private static readonly System.Text.RegularExpressions.Regex ProjectNamePattern =
        new("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,62}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex SensitiveAssignmentPattern =
        new(
            @"(?im)(\b(?:password|passwd|token|secret|api[_-]?key|authorization)\b\s*[:=]\s*)[^\r\n]*",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex UriCredentialPattern =
        new(
            @"(?i)([a-z][a-z0-9+.-]*://)[^\s/@:]+(?::[^\s/@]*)?@",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
        "healthcheck",
        "x-noctf-orchestration"
    };

    public static async Task<string> RunAsync(
        string composeYaml,
        string projectName,
        IReadOnlyList<string> args,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        ValidateProcessEnvironment(environmentVariables);
        var composeFile = Path.Combine(Path.GetTempPath(), $"noctf-compose-{Guid.NewGuid():N}.yml");

        try
        {
            await File.WriteAllTextAsync(composeFile, composeYaml, cancellationToken);
            var output = await RunDockerAsync(
                BuildArguments(composeFile, projectName, args),
                environmentVariables,
                cancellationToken);

            return output;
        }
        finally
        {
            DeleteTemporaryFileBestEffort(composeFile);
        }
    }

    internal static void DeleteTemporaryFileBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Cleanup must never replace the compose operation's result or exception.
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

    public static void ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName) || !ProjectNamePattern.IsMatch(projectName))
            throw new InvalidOperationException("Compose project name is invalid.");
    }

    /// <summary>
    /// Produces the exact compose document handed to Docker. Runtime identity is
    /// written into every service and project-owned resource so it survives a
    /// Runner restart and can be verified before a later mutation.
    /// </summary>
    internal static string ApplyRuntimeLabels(
        string composeYaml,
        IReadOnlyDictionary<string, string> runtimeLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(composeYaml);
        ArgumentNullException.ThrowIfNull(runtimeLabels);

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

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Compose YAML must contain exactly one mapping document.");
        if (TryGetMapping(root, "services") is not { } services || services.Children.Count == 0)
            throw new InvalidOperationException("Compose YAML must define at least one service.");

        foreach (var serviceNode in services.Children.Values)
        {
            if (serviceNode is not YamlMappingNode service)
                throw new InvalidOperationException("Compose service definitions must be mappings.");
            MergeLabels(service, runtimeLabels);
        }

        MergeProjectResourceLabels(root, "volumes", runtimeLabels, createDefault: false);
        MergeProjectResourceLabels(root, "networks", runtimeLabels, createDefault: true);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        yaml.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static void MergeProjectResourceLabels(
        YamlMappingNode root,
        string resourceKey,
        IReadOnlyDictionary<string, string> runtimeLabels,
        bool createDefault)
    {
        var resources = TryGetMapping(root, resourceKey);
        if (resources is null)
        {
            if (!createDefault)
                return;

            resources = new YamlMappingNode();
            SetMappingValue(root, resourceKey, resources);
        }

        if (createDefault && !resources.Children.Keys.Any(key =>
                ScalarValue(key).Equals("default", StringComparison.OrdinalIgnoreCase)))
        {
            resources.Add(new YamlScalarNode("default"), new YamlMappingNode());
        }

        foreach (var pair in resources.Children.ToArray())
        {
            YamlMappingNode resource;
            if (pair.Value is YamlMappingNode mapping)
            {
                resource = mapping;
            }
            else if (pair.Value is YamlScalarNode scalar && string.IsNullOrWhiteSpace(scalar.Value))
            {
                resource = new YamlMappingNode();
                resources.Children[pair.Key] = resource;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Compose top-level {resourceKey} must be mappings.");
            }

            MergeLabels(resource, runtimeLabels);
        }
    }

    private static void MergeLabels(
        YamlMappingNode owner,
        IReadOnlyDictionary<string, string> runtimeLabels)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var existing = GetValue(owner, "labels");
        switch (existing)
        {
            case null:
                break;
            case YamlMappingNode mapping:
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    var key = ScalarValue(keyNode);
                    if (string.IsNullOrWhiteSpace(key) || valueNode is not YamlScalarNode)
                        throw new InvalidOperationException("Compose labels must use scalar keys and values.");
                    merged[key] = ScalarValue(valueNode);
                }
                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                {
                    if (item is not YamlScalarNode)
                        throw new InvalidOperationException("Compose labels must be scalar values.");
                    var value = ScalarValue(item);
                    var separator = value.IndexOf('=', StringComparison.Ordinal);
                    var key = separator < 0 ? value : value[..separator];
                    if (string.IsNullOrWhiteSpace(key))
                        throw new InvalidOperationException("Compose label keys cannot be empty.");
                    merged[key] = separator < 0 ? string.Empty : value[(separator + 1)..];
                }
                break;
            default:
                throw new InvalidOperationException("Compose labels must be a mapping or a list.");
        }

        foreach (var (key, value) in runtimeLabels)
        {
            foreach (var existingKey in merged.Keys
                         .Where(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                merged.Remove(existingKey);
            }
            merged[key] = value;
        }

        var labels = new YamlMappingNode();
        foreach (var (key, value) in merged.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            labels.Add(new YamlScalarNode(key), new YamlScalarNode(value));
        SetMappingValue(owner, "labels", labels);
    }

    private static void SetMappingValue(YamlMappingNode mapping, string key, YamlNode value)
    {
        var existingKey = mapping.Children.Keys.FirstOrDefault(candidate =>
            ScalarValue(candidate).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existingKey is null)
            mapping.Add(new YamlScalarNode(key), value);
        else
            mapping.Children[existingKey] = value;
    }

    internal static void ValidateProcessEnvironment(
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is null)
            return;

        foreach (var key in environmentVariables.Keys)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                key.Contains('=') ||
                key.Any(char.IsControl) ||
                IsReservedProcessEnvironmentKey(key))
            {
                throw new InvalidOperationException(
                    $"Compose process environment variable '{key}' is not allowed.");
            }
        }
    }

    private static bool IsReservedProcessEnvironmentKey(string key)
    {
        string[] exact =
        [
            "PATH", "PATHEXT", "COMSPEC", "SYSTEMROOT", "WINDIR", "HOME",
            "USERPROFILE", "TMP", "TEMP", "TMPDIR", "HTTP_PROXY",
            "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "SSH_AUTH_SOCK"
        ];
        string[] prefixes = ["DOCKER_", "COMPOSE_", "BUILDX_", "BUILDKIT_", "XDG_", "LD_", "DYLD_"];
        return exact.Contains(key, StringComparer.OrdinalIgnoreCase) ||
               prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
        if (long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericUser))
            return numericUser > 0;

        return !first.Equals("root", StringComparison.OrdinalIgnoreCase);
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

    private static IReadOnlyList<string> BuildArguments(
        string composeFile,
        string projectName,
        IReadOnlyList<string> args)
    {
        var result = new List<string>(5 + args.Count)
        {
            "compose",
            "-f",
            composeFile,
            "-p",
            projectName
        };
        result.AddRange(args);
        return result;
    }

    private static async Task<string> RunDockerAsync(
        IReadOnlyList<string> arguments,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

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

        using var outputCts = new CancellationTokenSource();
        var stdoutTask = ReadBoundedOutputAsync(
            process.StandardOutput.BaseStream,
            MaxCapturedBytesPerStream,
            outputCts.Token);
        var stderrTask = ReadBoundedOutputAsync(
            process.StandardError.BaseStream,
            MaxCapturedBytesPerStream,
            outputCts.Token);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            outputCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(outputCts.Token);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException)
            {
            }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var diagnostic = SanitizeFailureDiagnostic(
                stderr.Text,
                environmentVariables?.Values);
            throw new InvalidOperationException(
                $"docker compose failed with exit code {process.ExitCode}: {diagnostic}");
        }

        return stdout.Text;
    }

    internal static async Task<BoundedProcessOutput> ReadBoundedOutputAsync(
        Stream stream,
        int maxCapturedBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapturedBytes);
        var captured = new MemoryStream(Math.Min(maxCapturedBytes, 65_536));
        var buffer = new byte[16_384];
        var truncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            var writable = Math.Min(read, maxCapturedBytes - (int)captured.Length);
            if (writable > 0)
                captured.Write(buffer, 0, writable);
            if (writable < read)
                truncated = true;
        }

        var text = Encoding.UTF8.GetString(captured.GetBuffer(), 0, (int)captured.Length);
        if (truncated)
            text += "\n[output truncated]";
        return new BoundedProcessOutput(text, truncated, (int)captured.Length);
    }

    internal static string SanitizeFailureDiagnostic(
        string diagnostic,
        IEnumerable<string>? sensitiveValues)
    {
        var sanitized = diagnostic;
        if (sensitiveValues is not null)
        {
            foreach (var value in sensitiveValues
                         .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 4)
                         .Distinct(StringComparer.Ordinal)
                         .OrderByDescending(value => value.Length))
            {
                sanitized = sanitized.Replace(value, "[redacted]", StringComparison.Ordinal);
            }
        }

        sanitized = UriCredentialPattern.Replace(sanitized, "$1[redacted]@");
        sanitized = SensitiveAssignmentPattern.Replace(sanitized, "$1[redacted]");
        sanitized = new string(sanitized
            .Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t')
            .ToArray())
            .Trim();
        if (sanitized.Length > MaxFailureDiagnosticCharacters)
        {
            sanitized = sanitized[..MaxFailureDiagnosticCharacters] +
                        "\n[diagnostic truncated]";
        }

        return string.IsNullOrWhiteSpace(sanitized)
            ? "diagnostic output unavailable"
            : sanitized;
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

    internal sealed record BoundedProcessOutput(string Text, bool Truncated, int CapturedBytes);
}
