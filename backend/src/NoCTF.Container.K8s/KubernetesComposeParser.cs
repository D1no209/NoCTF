using System.Globalization;
using System.Text.Json;
using NoCTF.PluginBase;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Container.K8s;

public sealed record KubernetesComposeService(
    string Name,
    string Image,
    string? Command,
    List<string> Entrypoint,
    Dictionary<string, string> Environment,
    Dictionary<string, string> Labels,
    List<int> Ports,
    List<string> DependsOn,
    OrchestrationSpec Orchestration);

public static class KubernetesComposeParser
{
    private static readonly HashSet<string> AllowedServiceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "command",
        "entrypoint",
        "environment",
        "labels",
        "ports",
        "volumes",
        "depends_on",
        "restart",
        "security_opt",
        "cap_drop",
        "read_only",
        "user",
        "pids_limit",
        "deploy",
        "x-noctf-orchestration"
    };

    public static IReadOnlyList<KubernetesComposeService> Parse(string composeYaml, OrchestrationSpec baseSpec)
    {
        if (string.IsNullOrWhiteSpace(composeYaml))
            throw new InvalidOperationException("Compose YAML cannot be empty.");

        var stream = new YamlStream();
        using var reader = new StringReader(composeYaml);
        stream.Load(reader);
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Compose YAML must be a mapping.");

        var services = GetMapping(root, "services")
            ?? throw new InvalidOperationException("Compose YAML must define services.");

        var result = new List<KubernetesComposeService>();
        foreach (var (nameNode, valueNode) in services.Children)
        {
            var serviceName = Scalar(nameNode);
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new InvalidOperationException("Compose service name cannot be empty.");
            if (valueNode is not YamlMappingNode service)
                throw new InvalidOperationException("Compose service definition must be a mapping.");

            foreach (var key in service.Children.Keys.Select(Scalar))
            {
                if (!AllowedServiceKeys.Contains(key))
                    throw new InvalidOperationException($"Compose service directive is not supported by Kubernetes runner: {key}");
            }

            var image = Scalar(GetValue(service, "image"));
            if (string.IsNullOrWhiteSpace(image))
                throw new InvalidOperationException("Compose service image is required.");

            var spec = CloneBaseSpec(baseSpec);
            ApplyServiceOrchestrationOverrides(spec, GetValue(service, "x-noctf-orchestration"));
            spec.Runtime = OrchestrationRuntimeKind.Compose;
            spec.Image = image;
            var command = Scalar(GetValue(service, "command"));
            if (!string.IsNullOrWhiteSpace(command))
                spec.Command = command;
            var entrypoint = ScalarList(GetValue(service, "entrypoint"));
            if (entrypoint.Count > 0)
                spec.Entrypoint = entrypoint;
            foreach (var (key, value) in ReadStringMap(GetValue(service, "environment")))
                spec.Environment[key] = value;
            var labels = ReadStringMap(GetValue(service, "labels"));
            var ports = ReadPorts(GetValue(service, "ports"));
            if (ports.Count > 0)
                spec.ExposedPort = ports.FirstOrDefault(port => port > 0);
            ApplyResources(spec, GetValue(service, "deploy"));
            ApplySecurity(spec, service);
            ValidatePidsLimit(GetValue(service, "pids_limit"));
            var volumes = ReadVolumes(GetValue(service, "volumes"));
            if (volumes.Count > 0)
                spec.Kubernetes.Volumes = volumes;
            NormalizeAndValidateKubernetesSpec(spec.Kubernetes);

            result.Add(new KubernetesComposeService(
                serviceName,
                image,
                spec.Command,
                spec.Entrypoint,
                spec.Environment,
                labels,
                ports,
                ScalarList(GetValue(service, "depends_on")),
                spec));
        }

        return result;
    }

    private static OrchestrationSpec CloneBaseSpec(OrchestrationSpec spec)
        => new()
        {
            Provider = spec.Provider,
            Runtime = spec.Runtime,
            Image = spec.Image,
            Command = spec.Command,
            Entrypoint = [.. spec.Entrypoint],
            Environment = new Dictionary<string, string>(spec.Environment, StringComparer.Ordinal),
            ExposedPort = spec.ExposedPort,
            ComposeYaml = spec.ComposeYaml,
            ComposeProjectName = spec.ComposeProjectName,
            Kubernetes = CloneKubernetesSpec(spec.Kubernetes)
        };

    private static KubernetesOrchestrationSpec CloneKubernetesSpec(KubernetesOrchestrationSpec spec)
        => new()
        {
            Exposure = spec.Exposure,
            NetworkMode = spec.NetworkMode,
            ImagePullPolicy = spec.ImagePullPolicy,
            ImagePullSecrets = [.. spec.ImagePullSecrets],
            Resources = new KubernetesResourceSpec
            {
                CpuRequest = spec.Resources.CpuRequest,
                MemoryRequest = spec.Resources.MemoryRequest,
                CpuLimit = spec.Resources.CpuLimit,
                MemoryLimit = spec.Resources.MemoryLimit,
                EphemeralStorageLimit = spec.Resources.EphemeralStorageLimit
            },
            Security = new KubernetesSecuritySpec
            {
                AllowPrivilegeEscalation = spec.Security.AllowPrivilegeEscalation,
                AutomountServiceAccountToken = spec.Security.AutomountServiceAccountToken,
                RunAsNonRoot = spec.Security.RunAsNonRoot,
                ReadOnlyRootFilesystem = spec.Security.ReadOnlyRootFilesystem,
                RunAsUser = spec.Security.RunAsUser,
                RunAsGroup = spec.Security.RunAsGroup,
                CapabilitiesDrop = [.. spec.Security.CapabilitiesDrop],
                CapabilitiesAdd = [.. spec.Security.CapabilitiesAdd]
            },
            Ingress = new KubernetesIngressSpec
            {
                Host = spec.Ingress.Host,
                BaseDomain = spec.Ingress.BaseDomain,
                ClassName = spec.Ingress.ClassName,
                TlsSecretName = spec.Ingress.TlsSecretName,
                Path = spec.Ingress.Path
            },
            NodeSelector = new Dictionary<string, string>(spec.NodeSelector, StringComparer.Ordinal),
            Tolerations = spec.Tolerations.Select(t => new KubernetesTolerationSpec
            {
                Key = t.Key,
                Operator = t.Operator,
                Value = t.Value,
                Effect = t.Effect
            }).ToList(),
            Annotations = new Dictionary<string, string>(spec.Annotations, StringComparer.Ordinal),
            Labels = new Dictionary<string, string>(spec.Labels, StringComparer.Ordinal),
            Volumes = spec.Volumes.Select(CloneVolume).ToList()
        };

    private static KubernetesVolumeSpec CloneVolume(KubernetesVolumeSpec volume)
        => new()
        {
            Name = volume.Name,
            MountPath = volume.MountPath,
            Type = volume.Type,
            ReadOnly = volume.ReadOnly,
            Data = new Dictionary<string, string>(volume.Data, StringComparer.Ordinal)
        };

    private static void ApplyResources(OrchestrationSpec spec, YamlNode? deployNode)
    {
        if (deployNode is not YamlMappingNode deploy)
            return;

        var resources = GetMapping(deploy, "resources");
        if (resources is null)
            return;

        var limits = GetMapping(resources, "limits");
        if (limits is not null)
        {
            var cpu = FirstNonEmpty(Scalar(GetValue(limits, "cpus")), Scalar(GetValue(limits, "cpu")));
            if (!string.IsNullOrWhiteSpace(cpu))
                spec.Kubernetes.Resources.CpuLimit = NormalizeCpu(cpu);

            var memory = FirstNonEmpty(Scalar(GetValue(limits, "memory")), Scalar(GetValue(limits, "mem_limit")));
            if (!string.IsNullOrWhiteSpace(memory))
                spec.Kubernetes.Resources.MemoryLimit = memory;
        }

        var reservations = GetMapping(resources, "reservations");
        if (reservations is not null)
        {
            var cpu = FirstNonEmpty(Scalar(GetValue(reservations, "cpus")), Scalar(GetValue(reservations, "cpu")));
            if (!string.IsNullOrWhiteSpace(cpu))
                spec.Kubernetes.Resources.CpuRequest = NormalizeCpu(cpu);

            var memory = Scalar(GetValue(reservations, "memory"));
            if (!string.IsNullOrWhiteSpace(memory))
                spec.Kubernetes.Resources.MemoryRequest = memory;
        }
    }

    private static void ApplySecurity(OrchestrationSpec spec, YamlMappingNode service)
    {
        var security = spec.Kubernetes.Security;
        var capDrop = ScalarList(GetValue(service, "cap_drop"));
        if (capDrop.Count > 0)
            security.CapabilitiesDrop = capDrop;

        if (GetValue(service, "cap_add") is not null)
            throw new InvalidOperationException("Kubernetes runner does not allow adding Linux capabilities.");

        if (ReadBool(GetValue(service, "read_only")) is { } readOnly)
            security.ReadOnlyRootFilesystem = readOnly;

        var user = Scalar(GetValue(service, "user"));
        if (!string.IsNullOrWhiteSpace(user))
            ApplyRunAsUser(security, user);

        foreach (var option in ScalarList(GetValue(service, "security_opt")))
        {
            var normalized = option.Trim();
            if (normalized.Equals("no-new-privileges:true", StringComparison.OrdinalIgnoreCase))
            {
                security.AllowPrivilegeEscalation = false;
                continue;
            }

            if (normalized.Equals("no-new-privileges:false", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Kubernetes runner does not allow disabling no-new-privileges.");

            throw new InvalidOperationException($"Compose security_opt is not supported by Kubernetes runner: {option}");
        }
    }

    private static List<KubernetesVolumeSpec> ReadVolumes(YamlNode? node)
    {
        if (node is null)
            return [];

        var volumes = new List<KubernetesVolumeSpec>();
        foreach (var item in SequenceOrScalar(node))
        {
            if (item is YamlScalarNode scalar)
            {
                volumes.Add(ParseShortVolume(scalar.Value ?? string.Empty));
                continue;
            }

            if (item is YamlMappingNode map)
            {
                volumes.Add(ParseLongVolume(map));
                continue;
            }

            throw new InvalidOperationException("Compose volume must be a scalar or mapping.");
        }

        return volumes;
    }

    private static KubernetesVolumeSpec ParseShortVolume(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Compose volume cannot be empty.");

        var parts = text.Split(':', StringSplitOptions.TrimEntries);
        var mountPath = parts.Length == 1 ? parts[0] : parts[1];
        if (string.IsNullOrWhiteSpace(mountPath) || !mountPath.StartsWith('/'))
            throw new InvalidOperationException("Kubernetes runner compose volumes must target an absolute container path.");

        var source = parts.Length == 1 ? string.Empty : parts[0];
        if (!string.IsNullOrWhiteSpace(source))
            ValidateVolumeName(source);
        var readOnly = parts.Length > 2 && parts[2].Split(',').Any(p => p.Equals("ro", StringComparison.OrdinalIgnoreCase));
        return new KubernetesVolumeSpec
        {
            Name = string.IsNullOrWhiteSpace(source) ? VolumeNameFromPath(mountPath) : source,
            MountPath = mountPath,
            Type = "emptyDir",
            ReadOnly = readOnly
        };
    }

    private static KubernetesVolumeSpec ParseLongVolume(YamlMappingNode map)
    {
        var type = Scalar(GetValue(map, "type"));
        var target = FirstNonEmpty(Scalar(GetValue(map, "target")), Scalar(GetValue(map, "destination")));
        var source = FirstNonEmpty(Scalar(GetValue(map, "source")), Scalar(GetValue(map, "name")));
        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith('/'))
            throw new InvalidOperationException("Kubernetes runner compose volumes must target an absolute container path.");

        var canonicalType = CanonicalVolumeType(type);
        var name = string.IsNullOrWhiteSpace(source) ? VolumeNameFromPath(target) : source;
        ValidateVolumeName(name);
        var data = ReadStringMap(GetValue(map, "data"));
        if ((canonicalType.Equals("secret", StringComparison.OrdinalIgnoreCase) ||
             canonicalType.Equals("configMap", StringComparison.OrdinalIgnoreCase)) &&
            data.Count == 0)
            throw new InvalidOperationException("Kubernetes runner data volumes must declare inline data.");

        return new KubernetesVolumeSpec
        {
            Name = name,
            MountPath = target,
            Type = canonicalType,
            ReadOnly = ReadBool(GetValue(map, "read_only")) ?? ReadBool(GetValue(map, "readOnly")) ?? canonicalType != "emptyDir",
            Data = data
        };
    }

    private static Dictionary<string, string> ReadStringMap(YamlNode? node)
    {
        if (node is null) return [];
        if (node is YamlMappingNode map)
        {
            return map.Children.ToDictionary(kvp => Scalar(kvp.Key), kvp => Scalar(kvp.Value), StringComparer.Ordinal);
        }

        if (node is YamlSequenceNode sequence)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in sequence.Children.Select(Scalar))
            {
                var parts = item.Split('=', 2);
                if (parts.Length == 2)
                    result[parts[0]] = parts[1];
            }
            return result;
        }

        return [];
    }

    private static List<int> ReadPorts(YamlNode? node)
    {
        if (node is null) return [];
        var result = new List<int>();
        foreach (var item in SequenceOrScalar(node))
        {
            var port = item switch
            {
                YamlScalarNode scalar => ParsePortScalar(scalar.Value ?? string.Empty),
                YamlMappingNode map => ParsePortMapping(map),
                _ => throw new InvalidOperationException("Compose port must be a scalar or mapping.")
            };
            if (port > 0)
                result.Add(port);
        }
        return result;
    }

    private static int ParsePortScalar(string value)
    {
        var text = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Compose port cannot be empty.");

        if (text.Contains('[', StringComparison.Ordinal) ||
            text.Count(c => c == ':') > 1)
            throw new InvalidOperationException("Kubernetes runner does not allow compose host port bindings.");

        var segments = text.Split(':', StringSplitOptions.TrimEntries);
        if (segments.Length > 1 && segments[0] != "0")
            throw new InvalidOperationException("Kubernetes runner does not allow fixed compose host ports.");

        var target = text.Split(':', StringSplitOptions.TrimEntries).Last();
        target = target.Split('/', 2, StringSplitOptions.TrimEntries)[0];
        if (target.Contains('-', StringComparison.Ordinal) ||
            !int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is <= 0 or > 65535)
            throw new InvalidOperationException($"Compose port is not supported by Kubernetes runner: {value}");

        return port;
    }

    private static int ParsePortMapping(YamlMappingNode map)
    {
        var protocol = Scalar(GetValue(map, "protocol"));
        if (!string.IsNullOrWhiteSpace(protocol) && !protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kubernetes runner only supports TCP compose ports.");
        if (!string.IsNullOrWhiteSpace(Scalar(GetValue(map, "host_ip"))))
            throw new InvalidOperationException("Kubernetes runner does not allow compose host_ip bindings.");
        if (Scalar(GetValue(map, "mode")).Equals("host", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kubernetes runner does not allow host-mode compose ports.");
        var published = Scalar(GetValue(map, "published"));
        if (!string.IsNullOrWhiteSpace(published) && published != "0")
            throw new InvalidOperationException("Kubernetes runner does not allow fixed compose host ports.");

        var target = FirstNonEmpty(Scalar(GetValue(map, "target")), Scalar(GetValue(map, "containerPort")));
        if (!int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is <= 0 or > 65535)
            throw new InvalidOperationException("Compose long-form port must define a valid target port.");

        return port;
    }

    private static void ValidatePidsLimit(YamlNode? node)
    {
        if (node is null)
            return;

        if (!int.TryParse(Scalar(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value is <= 0 or > 512)
        {
            throw new InvalidOperationException("Compose pids_limit must be between 1 and 512.");
        }
    }

    private static void ApplyServiceOrchestrationOverrides(OrchestrationSpec spec, YamlNode? node)
    {
        var json = Scalar(node);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("x-noctf-orchestration must be a JSON object.");

        var overrides = OrchestrationSpecSerializer.Read(json);
        if (HasProperty(doc.RootElement, "provider")) spec.Provider = overrides.Provider;
        if (HasProperty(doc.RootElement, "runtime")) spec.Runtime = overrides.Runtime;
        if (HasProperty(doc.RootElement, "image")) spec.Image = overrides.Image;
        if (HasProperty(doc.RootElement, "command")) spec.Command = overrides.Command;
        if (HasProperty(doc.RootElement, "entrypoint")) spec.Entrypoint = overrides.Entrypoint;
        if (HasProperty(doc.RootElement, "environment")) spec.Environment = overrides.Environment;
        if (HasProperty(doc.RootElement, "exposedPort")) spec.ExposedPort = overrides.ExposedPort;
        if (HasProperty(doc.RootElement, "composeYaml")) spec.ComposeYaml = overrides.ComposeYaml;
        if (HasProperty(doc.RootElement, "composeProjectName")) spec.ComposeProjectName = overrides.ComposeProjectName;
        if (TryGetProperty(doc.RootElement, "kubernetes", out var kubernetes))
            ApplyKubernetesOverrides(spec.Kubernetes, overrides.Kubernetes, kubernetes);
    }

    private static void ApplyKubernetesOverrides(
        KubernetesOrchestrationSpec target,
        KubernetesOrchestrationSpec overrides,
        JsonElement source)
    {
        if (HasProperty(source, "exposure")) target.Exposure = overrides.Exposure;
        if (HasProperty(source, "networkMode")) target.NetworkMode = overrides.NetworkMode;
        if (HasProperty(source, "imagePullPolicy")) target.ImagePullPolicy = overrides.ImagePullPolicy;
        if (HasProperty(source, "imagePullSecrets")) target.ImagePullSecrets = overrides.ImagePullSecrets;
        if (HasProperty(source, "resources")) target.Resources = overrides.Resources;
        if (HasProperty(source, "security")) target.Security = overrides.Security;
        if (HasProperty(source, "ingress")) target.Ingress = overrides.Ingress;
        if (HasProperty(source, "nodeSelector")) target.NodeSelector = overrides.NodeSelector;
        if (HasProperty(source, "tolerations")) target.Tolerations = overrides.Tolerations;
        if (HasProperty(source, "annotations")) target.Annotations = overrides.Annotations;
        if (HasProperty(source, "labels")) target.Labels = overrides.Labels;
        if (HasProperty(source, "volumes")) target.Volumes = overrides.Volumes;
    }

    private static void NormalizeAndValidateKubernetesSpec(KubernetesOrchestrationSpec spec)
    {
        var security = spec.Security;
        if (security.AllowPrivilegeEscalation)
            throw new InvalidOperationException("Kubernetes runner does not allow privilege escalation.");
        if (security.RunAsNonRoot == false)
            throw new InvalidOperationException("Kubernetes runner requires runAsNonRoot.");
        if (security.RunAsUser is 0 || security.RunAsGroup is 0)
            throw new InvalidOperationException("Kubernetes runner cannot run workloads as root.");
        if (security.CapabilitiesAdd.Count > 0)
            throw new InvalidOperationException("Kubernetes runner does not allow adding Linux capabilities.");

        security.RunAsNonRoot ??= true;
        security.RunAsUser ??= 1000;
        security.RunAsGroup ??= 1000;
        if (security.CapabilitiesDrop.Count == 0)
            security.CapabilitiesDrop = ["ALL"];

        foreach (var volume in spec.Volumes)
        {
            ValidateVolumeName(volume.Name);
            if ((volume.Type.Equals("secret", StringComparison.OrdinalIgnoreCase) ||
                 volume.Type.Equals("configMap", StringComparison.OrdinalIgnoreCase)) &&
                volume.Data.Count == 0)
                throw new InvalidOperationException("Kubernetes runner data volumes must declare inline data.");
        }
    }

    private static void ValidateVolumeName(string name)
    {
        var safe = KubernetesNames.SafeName(name);
        if (string.IsNullOrWhiteSpace(safe))
            throw new InvalidOperationException("Kubernetes volume name cannot be empty.");
        if (safe.StartsWith("registry-", StringComparison.OrdinalIgnoreCase) ||
            safe.StartsWith("noctf-", StringComparison.OrdinalIgnoreCase) ||
            safe.StartsWith("default-token", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Kubernetes volume name is reserved: {name}");
    }

    private static bool HasProperty(JsonElement element, string name)
        => TryGetProperty(element, name, out _);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static List<string> ScalarList(YamlNode? node)
        => node is null
            ? []
            : SequenceOrScalar(node).Select(Scalar).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();

    private static IEnumerable<YamlNode> SequenceOrScalar(YamlNode node)
        => node is YamlSequenceNode sequence
            ? sequence.Children
            : [node];

    private static YamlMappingNode? GetMapping(YamlMappingNode mapping, string key)
        => GetValue(mapping, key) as YamlMappingNode;

    private static YamlNode? GetValue(YamlMappingNode mapping, string key)
        => mapping.Children.FirstOrDefault(kvp =>
            Scalar(kvp.Key).Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string Scalar(YamlNode? node)
        => node switch
        {
            null => string.Empty,
            YamlScalarNode scalar => scalar.Value ?? string.Empty,
            _ => string.Empty
        };

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizeCpu(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith('m'))
            return trimmed;
        return decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu)
            ? $"{Math.Max(1, (int)Math.Round(cpu * 1000, MidpointRounding.AwayFromZero))}m"
            : trimmed;
    }

    private static bool? ReadBool(YamlNode? node)
    {
        var value = Scalar(node);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static void ApplyRunAsUser(KubernetesSecuritySpec security, string user)
    {
        var parts = user.Split(':', 2, StringSplitOptions.TrimEntries);
        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
            throw new InvalidOperationException("Kubernetes runner compose user must be a numeric uid[:gid].");
        if (uid == 0)
            throw new InvalidOperationException("Kubernetes runner compose user cannot run as root.");

        security.RunAsUser = uid;
        security.RunAsNonRoot = true;
        if (parts.Length == 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gid))
            security.RunAsGroup = gid;
    }

    private static string CanonicalVolumeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type) ||
            type.Equals("volume", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("tmpfs", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("emptyDir", StringComparison.OrdinalIgnoreCase))
            return "emptyDir";
        if (type.Equals("configMap", StringComparison.OrdinalIgnoreCase))
            return "configMap";
        if (type.Equals("secret", StringComparison.OrdinalIgnoreCase))
            return "secret";
        throw new InvalidOperationException($"Compose volume type is not supported by Kubernetes runner: {type}");
    }

    private static string VolumeNameFromPath(string path)
        => "vol-" + KubernetesNames.SafeName(path.Trim('/').Replace('/', '-'));
}
