using System.Globalization;
using NoCTF.Application.Runtime.Ports;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Application.Runtime.Configuration;

public static class ComposeRuntimeDefinitionPolicy
{
    private const int MaxComposeYamlLength = 1_048_576;
    private const int MaxServiceCount = 64;

    private static readonly string[] ForbiddenTopLevelFields =
    [
        "include",
        "volumes",
        "configs",
        "secrets",
        "name"
    ];

    private static readonly string[] ForbiddenServiceFields =
    [
        "network_mode",
        "pid",
        "ipc",
        "uts",
        "userns_mode",
        "volumes",
        "volumes_from",
        "tmpfs",
        "configs",
        "secrets",
        "devices",
        "device_cgroup_rules",
        "gpus",
        "build",
        "extends",
        "env_file",
        "label_file",
        "develop",
        "container_name",
        "scale",
        "runtime",
        "cap_add",
        "security_opt",
        "storage_opt",
        "sysctls",
        "external_links",
        "extra_hosts",
        "cgroup",
        "cgroup_parent",
        "credential_spec",
        "isolation",
        "logging",
        "profiles",
        "provider",
        "use_api_socket",
        "deploy",
        "mem_limit",
        "mem_reservation",
        "mem_swappiness",
        "memswap_limit",
        "cpus",
        "cpu_count",
        "cpu_percent",
        "cpu_period",
        "cpu_quota",
        "cpu_rt_period",
        "cpu_rt_runtime",
        "cpuset",
        "pids_limit",
        "ulimits",
        "shm_size",
        "oom_kill_disable",
        "oom_score_adj"
    ];

    public static IReadOnlyList<string> Validate(
        ComposeRuntimeDefinition definition,
        RuntimeResourceLimits totalLimits,
        IReadOnlyList<RuntimeUrlBinding>? urlBindings = null,
        RuntimeUrlBinding? controlCheckUrlBinding = null) =>
        Validate(
            definition.ComposeYaml,
            definition.ServiceResources,
            totalLimits,
            urlBindings,
            controlCheckUrlBinding);

    public static IReadOnlyList<string> Validate(
        string composeYaml,
        IReadOnlyDictionary<string, RuntimeResourceLimits>? serviceResources,
        RuntimeResourceLimits totalLimits,
        IReadOnlyList<RuntimeUrlBinding>? urlBindings = null,
        RuntimeUrlBinding? controlCheckUrlBinding = null)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(composeYaml))
        {
            errors.Add("Compose runtimes require ComposeYaml.");
            return errors;
        }
        if (composeYaml.Length > MaxComposeYamlLength)
        {
            errors.Add($"ComposeYaml cannot exceed {MaxComposeYamlLength} characters.");
            return errors;
        }

        var document = Load(composeYaml, errors);
        if (document is null)
            return errors;

        foreach (var field in ForbiddenTopLevelFields)
        {
            if (ContainsKey(document, field))
                errors.Add($"Compose top-level field '{field}' is not supported.");
        }

        ValidateNetworks(document, errors);
        if (!TryGetMapping(document, "services", out var services))
        {
            errors.Add("ComposeYaml requires a services mapping.");
            return errors;
        }
        if (services.Children.Count == 0)
        {
            errors.Add("ComposeYaml requires at least one service.");
            return errors;
        }
        if (services.Children.Count > MaxServiceCount)
            errors.Add($"ComposeYaml cannot contain more than {MaxServiceCount} services.");

        var serviceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in services.Children)
        {
            if (entry.Key is not YamlScalarNode { Value: { } serviceName }
                || string.IsNullOrWhiteSpace(serviceName))
            {
                errors.Add("Compose service names must be non-empty scalar values.");
                continue;
            }
            if (!IsServiceName(serviceName))
                errors.Add($"Compose service name '{serviceName}' is invalid.");
            if (!serviceNames.Add(serviceName))
                errors.Add($"Compose service name '{serviceName}' is duplicated.");
            if (entry.Value is not YamlMappingNode service)
            {
                errors.Add($"Compose service '{serviceName}' must be a mapping.");
                continue;
            }

            ValidateService(serviceName, service, errors);
        }

        ValidateResources(serviceNames, serviceResources, totalLimits, errors);
        ValidateBindings(
            serviceNames,
            (urlBindings ?? []).Append(controlCheckUrlBinding),
            errors);
        return errors;
    }

    public static string Prepare(ComposeRequest request)
    {
        var errors = Validate(
            request.ComposeYaml,
            request.ServiceResources,
            request.Limits,
            request.UrlBindings,
            request.ControlCheckUrlBinding);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var document = LoadRequired(request.ComposeYaml);
        var services = GetRequiredMapping(document, "services");
        var bindingsByService = (request.UrlBindings ?? [])
            .Append(request.ControlCheckUrlBinding)
            .Where(binding => binding is not null)
            .Select(binding => binding!)
            .GroupBy(binding => binding.ServiceName!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(binding => binding.ContainerPort!.Value)
                    .Distinct()
                    .Order()
                    .ToArray(),
                StringComparer.Ordinal);

        foreach (var entry in services.Children)
        {
            var serviceName = ((YamlScalarNode)entry.Key).Value!;
            var service = (YamlMappingNode)entry.Value;
            var limits = request.ServiceResources[serviceName];
            SetScalar(service, "mem_limit", limits.MemoryBytes.ToString(CultureInfo.InvariantCulture));
            SetScalar(
                service,
                "cpus",
                (limits.NanoCpus / 1_000_000_000m).ToString(
                    "0.#########",
                    CultureInfo.InvariantCulture));
            SetScalar(service, "pids_limit", limits.PidsLimit.ToString(CultureInfo.InvariantCulture));
            SetScalar(service, "privileged", "false");
            SetSequence(service, "cap_drop", ["ALL"]);
            SetSequence(service, "security_opt", ["no-new-privileges:true"]);
            MergeMappingValues(service, "environment", request.Environment);
            MergeMappingValues(service, "labels", request.Labels);

            Remove(service, "ports");
            if (bindingsByService.TryGetValue(serviceName, out var ports))
                SetSequence(
                    service,
                    "ports",
                    ports.Select(port => port.ToString(CultureInfo.InvariantCulture)));
        }

        ApplyNetworkLabels(document, request.Labels);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(document)).Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static YamlMappingNode? Load(string yaml, ICollection<string> errors)
    {
        try
        {
            using var reader = new StringReader(yaml);
            var stream = new YamlStream();
            stream.Load(reader);
            if (stream.Documents.Count != 1)
            {
                errors.Add("ComposeYaml must contain exactly one YAML document.");
                return null;
            }
            if (stream.Documents[0].RootNode is not YamlMappingNode document)
            {
                errors.Add("ComposeYaml root must be a mapping.");
                return null;
            }
            return document;
        }
        catch (YamlException exception)
        {
            errors.Add($"ComposeYaml is invalid: {exception.Message}");
            return null;
        }
    }

    private static YamlMappingNode LoadRequired(string yaml)
    {
        var errors = new List<string>();
        return Load(yaml, errors)
            ?? throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static void ValidateNetworks(YamlMappingNode document, ICollection<string> errors)
    {
        if (!TryGet(document, "networks", out var networksNode))
            return;
        if (networksNode is not YamlMappingNode networks)
        {
            errors.Add("Compose top-level networks must be a mapping.");
            return;
        }

        foreach (var entry in networks.Children)
        {
            if (entry.Key is not YamlScalarNode { Value: { } networkName }
                || entry.Value is not YamlMappingNode network)
            {
                errors.Add("Compose networks must use mapping definitions.");
                continue;
            }
            if ((TryGet(network, "external", out var external)
                    && !IsFalse(external))
                || ContainsKey(network, "name")
                || ContainsKey(network, "driver_opts"))
                errors.Add($"Compose network '{networkName}' cannot be external or explicitly named.");
            if (TryGetScalar(network, "driver", out var driver)
                && !string.Equals(driver, "bridge", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Compose network '{networkName}' must use the bridge driver.");
        }
    }

    private static void ValidateService(
        string serviceName,
        YamlMappingNode service,
        ICollection<string> errors)
    {
        foreach (var field in ForbiddenServiceFields)
        {
            if (ContainsKey(service, field))
                errors.Add($"Compose service '{serviceName}' field '{field}' is not supported.");
        }
        if (TryGet(service, "privileged", out var privileged)
            && !IsFalse(privileged))
            errors.Add($"Compose service '{serviceName}' cannot enable privileged mode.");
        if (!TryGetScalar(service, "image", out var image)
            || string.IsNullOrWhiteSpace(image))
            errors.Add($"Compose service '{serviceName}' requires an image.");
        if (TryGet(service, "environment", out var environment))
            ValidateKeyValues(serviceName, "environment", environment, "NOCTF_", errors);
        if (TryGet(service, "labels", out var labels))
            ValidateKeyValues(serviceName, "labels", labels, "noctf.io/", errors);
    }

    private static void ValidateResources(
        IReadOnlySet<string> serviceNames,
        IReadOnlyDictionary<string, RuntimeResourceLimits>? resources,
        RuntimeResourceLimits total,
        ICollection<string> errors)
    {
        if (resources is null || resources.Count == 0)
        {
            errors.Add("Compose service resource limits are required.");
            return;
        }

        foreach (var serviceName in serviceNames)
        {
            if (!resources.TryGetValue(serviceName, out var limits))
            {
                errors.Add($"Compose service '{serviceName}' requires resource limits.");
                continue;
            }
            if (limits.MemoryBytes <= 0 || limits.NanoCpus <= 0 || limits.PidsLimit <= 0)
                errors.Add($"Compose service '{serviceName}' resource limits must be positive.");
        }
        foreach (var resourceName in resources.Keys)
        {
            if (!serviceNames.Contains(resourceName))
                errors.Add($"Compose resource limits reference unknown service '{resourceName}'.");
        }

        try
        {
            var memory = resources.Values.Sum(limits => checked(limits.MemoryBytes));
            var cpus = resources.Values.Sum(limits => checked(limits.NanoCpus));
            var pids = resources.Values.Sum(limits => checked(limits.PidsLimit));
            if (memory > total.MemoryBytes || cpus > total.NanoCpus || pids > total.PidsLimit)
                errors.Add("Compose service resource limits exceed the runtime total limits.");
        }
        catch (OverflowException)
        {
            errors.Add("Compose service resource limits exceed the supported range.");
        }
    }

    private static void ValidateBindings(
        IReadOnlySet<string> serviceNames,
        IEnumerable<RuntimeUrlBinding?> bindings,
        ICollection<string> errors)
    {
        foreach (var binding in bindings.Where(binding => binding is not null).Select(binding => binding!))
        {
            if (binding.ServiceName is { } serviceName && !serviceNames.Contains(serviceName))
                errors.Add($"Compose URL binding references unknown service '{serviceName}'.");
        }
    }

    private static void ValidateKeyValues(
        string serviceName,
        string fieldName,
        YamlNode node,
        string reservedPrefix,
        ICollection<string> errors)
    {
        if (!TryReadKeyValues(node, out var values))
        {
            errors.Add(
                $"Compose service '{serviceName}' field '{fieldName}' must contain explicit key/value pairs.");
            return;
        }
        foreach (var key in values.Keys)
        {
            if (key.StartsWith(reservedPrefix, StringComparison.OrdinalIgnoreCase))
                errors.Add(
                    $"Compose service '{serviceName}' field '{fieldName}' cannot use reserved key '{key}'.");
        }
    }

    private static bool TryReadKeyValues(
        YamlNode node,
        out IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is YamlMappingNode mapping)
        {
            foreach (var entry in mapping.Children)
            {
                if (entry.Key is not YamlScalarNode { Value: { Length: > 0 } key }
                    || entry.Value is not YamlScalarNode { Value: { } value })
                {
                    values = result;
                    return false;
                }
                result[key] = value;
            }
            values = result;
            return true;
        }
        if (node is not YamlSequenceNode sequence)
        {
            values = result;
            return false;
        }
        foreach (var entry in sequence.Children)
        {
            if (entry is not YamlScalarNode { Value: { } item })
            {
                values = result;
                return false;
            }
            var separator = item.IndexOf('=');
            if (separator <= 0)
            {
                values = result;
                return false;
            }
            result[item[..separator]] = item[(separator + 1)..];
        }
        values = result;
        return true;
    }

    private static void MergeMappingValues(
        YamlMappingNode service,
        string fieldName,
        IReadOnlyDictionary<string, string> overrides)
    {
        var values = TryGet(service, fieldName, out var node)
            && TryReadKeyValues(node, out var configured)
                ? new Dictionary<string, string>(configured, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in overrides)
            values[pair.Key] = pair.Value;
        var mapping = new YamlMappingNode();
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            mapping.Add(pair.Key, pair.Value);
        Set(service, fieldName, mapping);
    }

    private static void ApplyNetworkLabels(
        YamlMappingNode document,
        IReadOnlyDictionary<string, string> labels)
    {
        if (!TryGet(document, "networks", out var networksNode))
        {
            var defaultNetwork = new YamlMappingNode();
            MergeMappingValues(defaultNetwork, "labels", labels);
            Set(document, "networks", new YamlMappingNode("default", defaultNetwork));
            return;
        }

        var networks = (YamlMappingNode)networksNode;
        foreach (var network in networks.Children.Values.Cast<YamlMappingNode>())
            MergeMappingValues(network, "labels", labels);
    }

    private static bool IsServiceName(string value) =>
        value.Length <= 63
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' or '.');

    private static bool IsFalse(YamlNode node) =>
        node is YamlScalarNode { Value: { } value }
        && bool.TryParse(value, out var parsed)
        && !parsed;

    private static bool ContainsKey(YamlMappingNode mapping, string key) =>
        mapping.Children.ContainsKey(new YamlScalarNode(key));

    private static bool TryGet(YamlMappingNode mapping, string key, out YamlNode value) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out value!);

    private static bool TryGetMapping(
        YamlMappingNode mapping,
        string key,
        out YamlMappingNode value)
    {
        if (TryGet(mapping, key, out var node) && node is YamlMappingNode child)
        {
            value = child;
            return true;
        }
        value = null!;
        return false;
    }

    private static YamlMappingNode GetRequiredMapping(YamlMappingNode mapping, string key) =>
        TryGetMapping(mapping, key, out var value)
            ? value
            : throw new InvalidOperationException($"Compose field '{key}' must be a mapping.");

    private static bool TryGetScalar(
        YamlMappingNode mapping,
        string key,
        out string value)
    {
        if (TryGet(mapping, key, out var node)
            && node is YamlScalarNode { Value: { } scalar })
        {
            value = scalar;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static void SetScalar(YamlMappingNode mapping, string key, string value) =>
        Set(mapping, key, new YamlScalarNode(value));

    private static void SetSequence(
        YamlMappingNode mapping,
        string key,
        IEnumerable<string> values) =>
        Set(mapping, key, new YamlSequenceNode(values.Select(value => new YamlScalarNode(value))));

    private static void Set(YamlMappingNode mapping, string key, YamlNode value) =>
        mapping.Children[new YamlScalarNode(key)] = value;

    private static void Remove(YamlMappingNode mapping, string key) =>
        mapping.Children.Remove(new YamlScalarNode(key));
}
