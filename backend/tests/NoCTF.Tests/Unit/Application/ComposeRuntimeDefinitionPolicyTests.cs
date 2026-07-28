using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
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

        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForDocker(request);
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
        await Assert.That(Scalar(
                Mapping(Mapping(root, "networks"), "challenge"),
                "internal"))
            .IsEqualTo("false");
    }

    [Test]
    public async Task Docker_preparation_rejects_InternetOnly_egress()
    {
        var request = new ComposeRequest(
            Guid.NewGuid(),
            RuntimeProvider.Docker,
            1,
            "noctf-runtime",
            "services:\n  web:\n    image: registry.example/web:v1",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, RuntimeResourceLimits> { ["web"] = ServiceLimits },
            ServiceLimits,
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(2),
            EgressPolicy: RuntimeEgressPolicy.InternetOnly);

        var action = () => ComposeRuntimeDefinitionPolicy.PrepareForDocker(request);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("does not support InternetOnly");
    }

    [Test]
    public async Task Kubernetes_preparation_omits_Docker_fields_and_exposes_bound_ports()
    {
        var request = new ComposeRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeProvider.Kubernetes,
            3,
            "noctf-runtime",
            """
            services:
              web:
                image: registry.example/web:v1
                ports:
                  - "127.0.0.1:9000:9000"
            """,
            new Dictionary<string, string> { ["FLAG"] = "flag-value" },
            new Dictionary<string, string> { ["noctf.io/managed"] = "true" },
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = new(268_435_456, 500_000_000, 0)
            },
            new(268_435_456, 500_000_000, 0),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(2),
            [
                new(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.Participants,
                    ContainerPort: 8080,
                    ServiceName: "web")
            ]);

        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            podPidsLimit: 512);
        var web = Mapping(Mapping(Load(prepared), "services"), "web");

        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("ports"))).IsFalse();
        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("mem_limit"))).IsFalse();
        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("cpus"))).IsFalse();
        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("pids_limit"))).IsFalse();
        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("security_opt"))).IsFalse();
        await Assert.That(
                Sequence(web, "expose").Children.Cast<YamlScalarNode>().Single().Value)
            .IsEqualTo("8080");
        await Assert.That(Scalar(Mapping(web, "environment"), "FLAG"))
            .IsEqualTo("flag-value");
        await Assert.That(Scalar(Mapping(web, "labels"), "noctf.io/managed"))
            .IsEqualTo("true");
    }

    [Test]
    public async Task Preparation_applies_service_environment_only_to_the_target_service()
    {
        var request = new ComposeRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeProvider.Docker,
            1,
            "noctf-runtime",
            """
            services:
              web:
                image: registry.example/web:v1
                environment:
                  FLAG: author-value
              worker:
                image: registry.example/worker:v1
            """,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = ServiceLimits,
                ["worker"] = ServiceLimits
            },
            new(536_870_912, 1_000_000_000, 256),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(2),
            ServiceEnvironment:
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["web"] = new Dictionary<string, string>
                    {
                        ["FLAG"] = "flag{fixed-team}"
                    }
                });

        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForDocker(request);
        var services = Mapping(Load(prepared), "services");
        var web = Mapping(services, "web");
        var worker = Mapping(services, "worker");

        await Assert.That(Scalar(Mapping(web, "environment"), "FLAG"))
            .IsEqualTo("flag{fixed-team}");
        await Assert.That(
                Mapping(worker, "environment").Children.ContainsKey(
                    new YamlScalarNode("FLAG")))
            .IsFalse();
    }

    [Test]
    public async Task Kubernetes_accepts_legacy_matching_PID_fields_and_removes_them()
    {
        var request = KubernetesRequest(
            """
            services:
              web:
                image: registry.example/web:v1
                pids_limit: 512
                deploy:
                  resources:
                    limits:
                      pids: 512
            """);

        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            podPidsLimit: 512);
        var web = Mapping(Mapping(Load(prepared), "services"), "web");

        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("pids_limit"))).IsFalse();
        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("deploy"))).IsFalse();
    }

    [Test]
    public async Task Kubernetes_accepts_one_replica_and_removes_the_deploy_field()
    {
        var request = KubernetesRequest(
            """
            services:
              web:
                image: registry.example/web:v1
                deploy:
                  replicas: 1
            """);

        var prepared = ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            podPidsLimit: 512);
        var web = Mapping(Mapping(Load(prepared), "services"), "web");

        await Assert.That(web.Children.ContainsKey(new YamlScalarNode("deploy"))).IsFalse();
    }

    [Test]
    public async Task Kubernetes_rejects_more_than_one_replica()
    {
        var request = KubernetesRequest(
            """
            services:
              web:
                image: registry.example/web:v1
                deploy:
                  replicas: 2
            """);

        var action = () => ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            podPidsLimit: 512);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("must use exactly one replica");
    }

    [Test]
    public async Task Kubernetes_rejects_a_legacy_PID_field_that_differs_from_the_pool()
    {
        var request = KubernetesRequest(
            """
            services:
              web:
                image: registry.example/web:v1
                pids_limit: 256
            """);

        var action = () => ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            podPidsLimit: 512);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message)
            .Contains("declares a PID limit that differs from the platform limit");
    }

    [Arguments("web_api")]
    [Arguments("Web")]
    [Arguments("1web")]
    [Arguments("web.")]
    [Arguments("web-")]
    [Test]
    public async Task Kubernetes_rejects_non_portable_DNS_service_names(string serviceName)
    {
        var request = KubernetesRequest(
            $$"""
            services:
              {{serviceName}}:
                image: registry.example/web:v1
            """) with
        {
            ServiceResources = new Dictionary<string, RuntimeResourceLimits>
            {
                [serviceName] = new(268_435_456, 500_000_000, 0)
            }
        };

        var action = () => ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            podPidsLimit: 512);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("DNS-1123 label");
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

    private static ComposeRequest KubernetesRequest(string composeYaml) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeProvider.Kubernetes,
            3,
            "noctf-runtime",
            composeYaml,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, RuntimeResourceLimits>
            {
                ["web"] = new(268_435_456, 500_000_000, 0)
            },
            new(268_435_456, 500_000_000, 0),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(2));
}
