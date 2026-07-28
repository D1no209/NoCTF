using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class DockerComposeIngressProxyPolicyTests
{
    [Test]
    public async Task Proxy_is_the_only_service_joined_to_the_platform_network()
    {
        var request = new ComposeRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeProvider.Docker,
            3,
            "runtime",
            """
            services:
              web:
                image: registry.example/web:v1
              db:
                image: registry.example/db:v1
            """,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/runtime-instance-id"] =
                    "11111111-1111-1111-1111-111111111111",
                ["noctf.io/generation"] = "3"
            },
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = new(268_435_456, 500_000_000, 128),
                ["db"] = new(268_435_456, 500_000_000, 128)
            },
            new(536_870_912, 1_000_000_000, 256),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(2),
            [
                new RuntimeUrlBinding(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.Participants,
                    ContainerPort: 8080,
                    ServiceName: "web")
            ]);
        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForDocker(request);

        var plan = DockerComposeIngressProxyPolicy.Apply(
            prepared,
            request,
            new DockerRuntimeOptions(
                NetworkName: "noctf-network",
                IngressProxyImage: "haproxy:test"));
        var root = Load(plan.ComposeYaml);
        var services = Mapping(root, "services");
        var networks = Mapping(root, "networks");
        var proxy = Mapping(services, DockerComposeIngressProxyPolicy.ProxyServiceName);

        await Assert.That(plan.Bindings)
            .IsEquivalentTo([new DockerComposeIngressBinding("web", 8080, 20_000)]);
        await Assert.That(Scalar(Mapping(
                networks,
                DockerComposeIngressProxyPolicy.PlatformNetworkKey),
                "name"))
            .IsEqualTo("noctf-network");
        await Assert.That(Scalar(
                Mapping(networks, DockerComposeIngressProxyPolicy.PlatformNetworkKey),
                "external"))
            .IsEqualTo("true");
        await Assert.That(Sequence(proxy, "networks").Children
                .Cast<YamlScalarNode>()
                .Select(node => node.Value!))
            .IsEquivalentTo(["default", DockerComposeIngressProxyPolicy.PlatformNetworkKey]);
        await Assert.That(Sequence(proxy, "command").Children
                .Cast<YamlScalarNode>()
                .Single()
                .Value)
            .Contains("server target web:8080");
        await Assert.That(Mapping(services, "web").Children
                .ContainsKey(new YamlScalarNode("networks")))
            .IsFalse();
    }

    private static YamlMappingNode Load(string yaml)
    {
        using var reader = new StringReader(yaml);
        var stream = new YamlStream();
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents.Single().RootNode;
    }

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        (YamlMappingNode)parent.Children[new YamlScalarNode(key)];

    private static YamlSequenceNode Sequence(YamlMappingNode parent, string key) =>
        (YamlSequenceNode)parent.Children[new YamlScalarNode(key)];

    private static string? Scalar(YamlMappingNode parent, string key) =>
        ((YamlScalarNode)parent.Children[new YamlScalarNode(key)]).Value;
}
