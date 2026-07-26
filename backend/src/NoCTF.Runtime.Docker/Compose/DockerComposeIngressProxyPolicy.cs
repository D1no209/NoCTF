using System.Globalization;
using System.Text;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Runtime.Docker.Containers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Runtime.Docker.Compose;

public sealed record DockerComposeIngressBinding(
    string TargetService,
    int TargetPort,
    int ListenerPort);

public sealed record DockerComposeIngressPlan(
    string ComposeYaml,
    string? ProxyServiceName,
    IReadOnlyList<DockerComposeIngressBinding> Bindings);

public static class DockerComposeIngressProxyPolicy
{
    public const string ProxyServiceName = "noctf-ingress";
    public const string PlatformNetworkKey = "noctf-platform";
    private const int FirstListenerPort = 20_000;

    public static DockerComposeIngressPlan Apply(
        string composeYaml,
        ComposeRequest request,
        DockerRuntimeOptions options)
    {
        var bindings = (request.UrlBindings ?? [])
            .Select(binding => new
            {
                Service = binding.ServiceName
                    ?? throw new InvalidOperationException(
                        "Docker ingress binding requires ServiceName."),
                Port = binding.ContainerPort
                    ?? throw new InvalidOperationException(
                        "Docker ingress binding requires ContainerPort.")
            })
            .Distinct()
            .OrderBy(binding => binding.Service, StringComparer.Ordinal)
            .ThenBy(binding => binding.Port)
            .Select((binding, index) => new DockerComposeIngressBinding(
                binding.Service,
                binding.Port,
                checked(FirstListenerPort + index)))
            .ToArray();
        if (bindings.Length == 0)
            return new(composeYaml, null, []);
        if (bindings[^1].ListenerPort > 65_535)
            throw new InvalidOperationException(
                "Docker ingress proxy cannot expose more than 45536 distinct bindings.");
        if (string.IsNullOrWhiteSpace(options.NetworkName))
            throw new InvalidOperationException(
                "Docker ingress proxy requires the platform network.");
        if (string.IsNullOrWhiteSpace(options.IngressProxyImage))
            throw new InvalidOperationException(
                "Docker ingress proxy image is required.");

        var document = Load(composeYaml);
        var services = Mapping(document, "services");
        var networks = Mapping(document, "networks");
        if (services.Children.ContainsKey(new YamlScalarNode(ProxyServiceName)))
            throw new InvalidOperationException(
                $"Compose service name '{ProxyServiceName}' is reserved by the platform.");
        if (networks.Children.ContainsKey(new YamlScalarNode(PlatformNetworkKey)))
            throw new InvalidOperationException(
                $"Compose network name '{PlatformNetworkKey}' is reserved by the platform.");

        var internalNetworks = networks.Children.Keys
            .Cast<YamlScalarNode>()
            .Select(key => key.Value!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        networks.Add(
            PlatformNetworkKey,
            new YamlMappingNode
            {
                { "external", "true" },
                { "name", options.NetworkName }
            });

        var labels = new YamlMappingNode();
        foreach (var pair in request.Labels.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            labels.Add(pair.Key, pair.Value);
        labels.Add("noctf.io/resource-role", "ingress-proxy");

        var proxy = new YamlMappingNode
        {
            { "image", options.IngressProxyImage },
            { "entrypoint", new YamlSequenceNode("/bin/sh", "-ec") },
            {
                "command",
                new YamlSequenceNode(new YamlScalarNode(BuildStartupScript(bindings))
                {
                    Style = ScalarStyle.Literal
                })
            },
            {
                "ports",
                new YamlSequenceNode(bindings.Select(binding =>
                    new YamlScalarNode(binding.ListenerPort.ToString(
                        CultureInfo.InvariantCulture))))
            },
            {
                "networks",
                new YamlSequenceNode(
                    internalNetworks
                        .Append(PlatformNetworkKey)
                        .Select(name => new YamlScalarNode(name)))
            },
            {
                "depends_on",
                new YamlSequenceNode(
                    bindings
                        .Select(binding => binding.TargetService)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .Select(name => new YamlScalarNode(name)))
            },
            { "labels", labels },
            { "mem_limit", options.IngressProxyMemoryBytes.ToString(CultureInfo.InvariantCulture) },
            {
                "cpus",
                (options.IngressProxyNanoCpus / 1_000_000_000m).ToString(
                    "0.#########",
                    CultureInfo.InvariantCulture)
            },
            { "pids_limit", options.IngressProxyPidsLimit.ToString(CultureInfo.InvariantCulture) },
            { "privileged", "false" },
            { "read_only", "true" },
            { "cap_drop", new YamlSequenceNode("ALL") },
            { "security_opt", new YamlSequenceNode("no-new-privileges:true") },
            { "tmpfs", new YamlSequenceNode("/tmp:size=16m,mode=1777") }
        };
        services.Add(ProxyServiceName, proxy);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(document)).Save(writer, assignAnchors: false);
        return new(writer.ToString(), ProxyServiceName, bindings);
    }

    private static string BuildStartupScript(
        IReadOnlyList<DockerComposeIngressBinding> bindings)
    {
        var config = new StringBuilder()
            .AppendLine("global")
            .AppendLine("  log stdout format raw local0")
            .AppendLine("defaults")
            .AppendLine("  mode tcp")
            .AppendLine("  timeout connect 5s")
            .AppendLine("  timeout client 1h")
            .AppendLine("  timeout server 1h");
        foreach (var binding in bindings)
        {
            config
                .Append("frontend ingress_").Append(binding.ListenerPort).AppendLine()
                .Append("  bind :").Append(binding.ListenerPort).AppendLine()
                .Append("  default_backend target_").Append(binding.ListenerPort).AppendLine()
                .Append("backend target_").Append(binding.ListenerPort).AppendLine()
                .Append("  server target ")
                .Append(binding.TargetService)
                .Append(':')
                .Append(binding.TargetPort)
                .AppendLine();
        }
        return $"""
            cat > /tmp/noctf-haproxy.cfg <<'NOCTF_HAPROXY'
            {config.ToString().TrimEnd()}
            NOCTF_HAPROXY
            exec haproxy -f /tmp/noctf-haproxy.cfg
            """;
    }

    private static YamlMappingNode Load(string yaml)
    {
        using var reader = new StringReader(yaml);
        var stream = new YamlStream();
        stream.Load(reader);
        return stream.Documents.Single().RootNode as YamlMappingNode
            ?? throw new InvalidOperationException("Compose root must be a mapping.");
    }

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        parent.Children.TryGetValue(new YamlScalarNode(key), out var value)
        && value is YamlMappingNode mapping
            ? mapping
            : throw new InvalidOperationException(
                $"Compose field '{key}' must be a mapping.");
}
