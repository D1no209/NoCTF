using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Tests.Unit.Application;

public sealed class ComposeRuntimeDefinitionPolicyTests
{
    private static readonly RuntimeResourceLimits ServiceLimits =
        new(268_435_456, 500_000_000, 128);

    [Test]
    public async Task Safe_definition_requires_exact_per_service_resources_within_total()
    {
        var definition = new ComposeRuntimeDefinition(
            """
            services:
              web:
                image: registry.example/web:v1
              cache:
                image: registry.example/cache:v1
            networks:
              challenge:
                driver: bridge
            """,
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = ServiceLimits,
                ["cache"] = ServiceLimits
            });

        var errors = ComposeRuntimeDefinitionPolicy.Validate(
            definition,
            new(536_870_912, 1_000_000_000, 256));

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Definition_rejects_dangerous_compose_features()
    {
        var definition = new ComposeRuntimeDefinition(
            """
            services:
              web:
                image: registry.example/web:v1
                privileged: true
                network_mode: host
                volumes:
                  - /var/run/docker.sock:/var/run/docker.sock
                devices:
                  - /dev/kvm
                cap_add:
                  - SYS_ADMIN
            volumes:
              data: {}
            """,
            new Dictionary<string, RuntimeResourceLimits> { ["web"] = ServiceLimits });

        var errors = ComposeRuntimeDefinitionPolicy.Validate(definition, ServiceLimits);

        await Assert.That(errors)
            .Contains("Compose top-level field 'volumes' is not supported.");
        await Assert.That(errors)
            .Contains("Compose service 'web' cannot enable privileged mode.");
        await Assert.That(errors)
            .Contains("Compose service 'web' field 'network_mode' is not supported.");
        await Assert.That(errors)
            .Contains("Compose service 'web' field 'volumes' is not supported.");
        await Assert.That(errors)
            .Contains("Compose service 'web' field 'devices' is not supported.");
        await Assert.That(errors)
            .Contains("Compose service 'web' field 'cap_add' is not supported.");
    }

    [Test]
    public async Task Definition_rejects_missing_unknown_and_over_budget_service_resources()
    {
        var definition = new ComposeRuntimeDefinition(
            """
            services:
              web:
                image: registry.example/web:v1
              cache:
                image: registry.example/cache:v1
            """,
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = ServiceLimits,
                ["unknown"] = ServiceLimits
            });

        var errors = ComposeRuntimeDefinitionPolicy.Validate(definition, ServiceLimits);

        await Assert.That(errors)
            .Contains("Compose service 'cache' requires resource limits.");
        await Assert.That(errors)
            .Contains("Compose resource limits reference unknown service 'unknown'.");
        await Assert.That(errors)
            .Contains("Compose service resource limits exceed the runtime total limits.");
    }

    [Test]
    public async Task Prepare_overrides_platform_values_and_publishes_only_bound_ports()
    {
        var request = new ComposeRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeProvider.Docker,
            3,
            "noctf-runtime",
            """
            services:
              web:
                image: registry.example/web:v1
                environment:
                  MODE: challenge
                labels:
                  author: noctf
                ports:
                  - "127.0.0.1:9000:9000"
              worker:
                image: registry.example/worker:v1
            networks:
              challenge:
                driver: bridge
            """,
            new Dictionary<string, string>
            {
                ["MODE"] = "platform",
                ["FLAG"] = "flag-value"
            },
            new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/generation"] = "3"
            },
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = ServiceLimits,
                ["worker"] = new(134_217_728, 250_000_000, 64)
            },
            new(402_653_184, 750_000_000, 192),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(2),
            [
                new(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.Participants,
                    ContainerPort: 8080,
                    ServiceName: "web")
            ]);

        var prepared = ComposeRuntimeDefinitionPolicy.Prepare(request);
        var root = Load(prepared);
        var services = Mapping(root, "services");
        var web = Mapping(services, "web");
        var worker = Mapping(services, "worker");
        var environment = Mapping(web, "environment");
        var labels = Mapping(web, "labels");
        var ports = Sequence(web, "ports");

        await Assert.That(Scalar(environment, "MODE")).IsEqualTo("platform");
        await Assert.That(Scalar(environment, "FLAG")).IsEqualTo("flag-value");
        await Assert.That(Scalar(labels, "author")).IsEqualTo("noctf");
        await Assert.That(Scalar(labels, "noctf.io/managed")).IsEqualTo("true");
        await Assert.That(Scalar(web, "mem_limit")).IsEqualTo("268435456");
        await Assert.That(Scalar(web, "cpus")).IsEqualTo("0.5");
        await Assert.That(Scalar(web, "pids_limit")).IsEqualTo("128");
        await Assert.That(Scalar(web, "privileged")).IsEqualTo("false");
        await Assert.That(ports.Children.Cast<YamlScalarNode>().Single().Value)
            .IsEqualTo("8080");
        await Assert.That(worker.Children.ContainsKey(new YamlScalarNode("ports"))).IsFalse();
        await Assert.That(
                Sequence(web, "cap_drop").Children.Cast<YamlScalarNode>().Single().Value)
            .IsEqualTo("ALL");
        await Assert.That(
                Sequence(web, "security_opt").Children.Cast<YamlScalarNode>().Single().Value)
            .IsEqualTo("no-new-privileges:true");
        var networkLabels = Mapping(
            Mapping(Mapping(root, "networks"), "challenge"),
            "labels");
        await Assert.That(Scalar(networkLabels, "noctf.io/managed")).IsEqualTo("true");
    }

    private static YamlMappingNode Load(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents.Single().RootNode;
    }

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        (YamlMappingNode)parent.Children[new YamlScalarNode(key)];

    private static YamlSequenceNode Sequence(YamlMappingNode parent, string key) =>
        (YamlSequenceNode)parent.Children[new YamlScalarNode(key)];

    private static string? Scalar(YamlMappingNode parent, string key) =>
        ((YamlScalarNode)parent.Children[new YamlScalarNode(key)]).Value;
}
