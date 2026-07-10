using NoCTF.Container.K8s;
using NoCTF.PluginBase;
using System.Text.Json;

namespace NoCTF.Tests;

public class KubernetesManifestFactoryTests
{
    [Fact]
    public void NetworkPolicies_DefaultToIsolatedDnsAndSameInstanceOnly()
    {
        var policies = KubernetesManifestFactory.NetworkPolicies("noctf-inst-test", OrchestrationNetworkMode.Isolated);

        Assert.Contains(policies, p => p.Metadata.Name == "default-deny");
        var allow = Assert.Single(policies, p => p.Metadata.Name == "allow-same-instance");
        Assert.Equal(2, allow.Spec.Egress.Count);
        var dnsRule = Assert.Single(allow.Spec.Egress, rule =>
            rule.Ports?.Any(port => port.Port.Value == "53") == true);
        var dnsPeer = Assert.Single(dnsRule.To);
        Assert.Equal("kube-system", dnsPeer.NamespaceSelector.MatchLabels["kubernetes.io/metadata.name"]);
        Assert.Equal("kube-dns", dnsPeer.PodSelector.MatchLabels["k8s-app"]);
        Assert.DoesNotContain(policies, p => p.Metadata.Name == "allow-open-egress");
    }

    [Fact]
    public void ResolveExposure_UsesRunnerDefaultWhenSpecDoesNotOverride()
    {
        var exposure = KubernetesManifestFactory.ResolveExposure(
            new OrchestrationSpec(),
            new KubernetesRunnerOptions { DefaultExposure = "Ingress" });

        Assert.Equal(OrchestrationExposureType.Ingress, exposure);
    }

    [Fact]
    public void RegistrySecret_ContainsDockerConfigJsonPayload()
    {
        var secret = KubernetesRegistrySecretFactory.Build("ns", new KubernetesRegistryCredential
        {
            Registry = "registry.example.com",
            UserName = "player",
            Password = "secret"
        });

        Assert.Equal("kubernetes.io/dockerconfigjson", secret.Type);
        Assert.Contains(".dockerconfigjson", secret.Data.Keys);
        Assert.StartsWith("registry-player-", secret.Metadata.Name);
        using var doc = JsonDocument.Parse(secret.Data[".dockerconfigjson"]);
        Assert.True(doc.RootElement.TryGetProperty("auths", out var auths));
        Assert.True(auths.GetProperty("registry.example.com").TryGetProperty("username", out _));
    }

    [Fact]
    public void Deployment_UsesSafeSecurityDefaults()
    {
        var deployment = KubernetesManifestFactory.Deployment(
            "ns",
            "web",
            new ContainerConfig("nginx:alpine", PortMappings: new Dictionary<int, int> { [80] = 0 }),
            new KubernetesRunnerOptions(),
            new OrchestrationSpec { ExposedPort = 80 });

        var container = Assert.Single(deployment.Spec.Template.Spec.Containers);
        Assert.False(container.SecurityContext.AllowPrivilegeEscalation);
        Assert.True(container.SecurityContext.RunAsNonRoot);
        Assert.Equal(1000, container.SecurityContext.RunAsUser);
        Assert.Equal(1000, container.SecurityContext.RunAsGroup);
        Assert.Contains("ALL", container.SecurityContext.Capabilities.Drop);
        Assert.Null(container.SecurityContext.Capabilities.Add);
        Assert.False(deployment.Spec.Template.Spec.AutomountServiceAccountToken);
        Assert.Equal(80, Assert.Single(container.Ports).ContainerPort);
    }

    [Fact]
    public void Deployment_RunsShellCommandWhenEntrypointIsNotSet()
    {
        var deployment = KubernetesManifestFactory.Deployment(
            "ns",
            "checker",
            new ContainerConfig("alpine:latest", Command: "echo ok"),
            new KubernetesRunnerOptions(),
            new OrchestrationSpec());

        var container = Assert.Single(deployment.Spec.Template.Spec.Containers);
        Assert.Equal(["/bin/sh", "-c"], container.Command);
        Assert.Equal(["echo ok"], container.Args);
    }

    [Fact]
    public void Deployment_DoesNotAllowUserLabelsToOverrideSelectors()
    {
        var deployment = KubernetesManifestFactory.Deployment(
            "ns",
            "web",
            new ContainerConfig("nginx:alpine", Labels: new Dictionary<string, string>
            {
                ["app"] = "wrong",
                [KubernetesManifestFactory.InstanceLabel] = "wrong"
            }),
            new KubernetesRunnerOptions(),
            new OrchestrationSpec
            {
                Kubernetes = new KubernetesOrchestrationSpec
                {
                    Labels = new Dictionary<string, string>
                    {
                        ["app"] = "also-wrong",
                        [KubernetesManifestFactory.ServiceLabel] = "also-wrong"
                    }
                }
            });

        Assert.Equal("web", deployment.Spec.Template.Metadata.Labels["app"]);
        Assert.Equal("web", deployment.Spec.Template.Metadata.Labels[KubernetesManifestFactory.InstanceLabel]);
        Assert.False(deployment.Spec.Template.Metadata.Labels.ContainsKey(KubernetesManifestFactory.ServiceLabel));
    }

    [Fact]
    public void ExposedIngressPolicy_AllowsOnlyRequestedPort()
    {
        var policy = KubernetesManifestFactory.ExposedIngressPolicy("ns", "web", 8080);

        Assert.Equal("allow-exposed-web-8080", policy.Metadata.Name);
        Assert.Equal("web", policy.Spec.PodSelector.MatchLabels["app"]);
        var rule = Assert.Single(policy.Spec.Ingress);
        Assert.Equal("8080", rule.Ports.Single().Port.Value);
        Assert.Contains(rule.FromProperty, peer => peer.NamespaceSelector is not null);
    }

    [Fact]
    public void Deployment_PreservesQualifiedNoctfLabels()
    {
        var deployment = KubernetesManifestFactory.Deployment(
            "ns",
            "web",
            new ContainerConfig("nginx:alpine"),
            new KubernetesRunnerOptions(),
            new OrchestrationSpec(),
            new Dictionary<string, string>
            {
                [KubernetesManifestFactory.ProjectLabel] = "range",
                [KubernetesManifestFactory.ServiceLabel] = "web"
            });

        Assert.Equal("range", deployment.Spec.Template.Metadata.Labels[KubernetesManifestFactory.ProjectLabel]);
        Assert.Equal("web", deployment.Spec.Template.Metadata.Labels[KubernetesManifestFactory.ServiceLabel]);
    }

    [Fact]
    public void ComposeParser_RejectsUnsupportedServiceDirectives()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    privileged: true
""";

        Assert.Throws<InvalidOperationException>(() => KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));
    }

    [Fact]
    public void ComposeParser_RejectsCapabilityAdd()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    cap_add:
      - SYS_ADMIN
""";

        Assert.Throws<InvalidOperationException>(() => KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));
    }

    [Fact]
    public void ComposeParser_RejectsSecretVolumeWithoutInlineData()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    volumes:
      - type: secret
        source: app-secret
        target: /etc/secret
""";

        Assert.Throws<InvalidOperationException>(() => KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));
    }

    [Fact]
    public void ComposeParser_RejectsReservedSecretVolumeNames()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    volumes:
      - type: secret
        source: registry-private
        target: /etc/secret
        data:
          password: value
""";

        Assert.Throws<InvalidOperationException>(() => KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));
    }

    [Fact]
    public void ComposeParser_MapsSafeResourcesSecurityAndVolumes()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    user: "1000:1000"
    read_only: true
    cap_drop:
      - NET_RAW
    deploy:
      resources:
        limits:
          cpus: "0.5"
          memory: 256Mi
        reservations:
          cpus: "0.1"
          memory: 64Mi
    volumes:
      - type: configMap
        source: app-config
        target: /etc/app
        data:
          config.json: "{}"
      - cache:/var/cache:ro
""";

        var service = Assert.Single(KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));

        Assert.Equal("500m", service.Orchestration.Kubernetes.Resources.CpuLimit);
        Assert.Equal("100m", service.Orchestration.Kubernetes.Resources.CpuRequest);
        Assert.Equal("256Mi", service.Orchestration.Kubernetes.Resources.MemoryLimit);
        Assert.Equal("64Mi", service.Orchestration.Kubernetes.Resources.MemoryRequest);
        Assert.True(service.Orchestration.Kubernetes.Security.RunAsNonRoot);
        Assert.Equal(1000, service.Orchestration.Kubernetes.Security.RunAsUser);
        Assert.Equal(1000, service.Orchestration.Kubernetes.Security.RunAsGroup);
        Assert.True(service.Orchestration.Kubernetes.Security.ReadOnlyRootFilesystem);
        Assert.Contains("NET_RAW", service.Orchestration.Kubernetes.Security.CapabilitiesDrop);
        Assert.Collection(
            service.Orchestration.Kubernetes.Volumes,
            volume =>
            {
                Assert.Equal("app-config", volume.Name);
                Assert.Equal("configMap", volume.Type);
                Assert.Equal("/etc/app", volume.MountPath);
                Assert.Equal("{}", volume.Data["config.json"]);
            },
            volume =>
            {
                Assert.Equal("cache", volume.Name);
                Assert.Equal("emptyDir", volume.Type);
                Assert.True(volume.ReadOnly);
            });
    }

    [Fact]
    public void ComposeParser_ReadsLongFormAndProtocolPorts()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    ports:
      - "80/tcp"
      - target: 8443
        protocol: tcp
""";

        var service = Assert.Single(KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));

        Assert.Equal([80, 8443], service.Ports);
    }

    [Fact]
    public void ComposeParser_AppliesPerServiceOrchestrationOverrides()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    x-noctf-orchestration: |-
      {"kubernetes":{"exposure":"Ingress","resources":{"cpuLimit":"750m","memoryLimit":"512Mi"}}}
""";

        var service = Assert.Single(KubernetesComposeParser.Parse(yaml, new OrchestrationSpec
        {
            Kubernetes = new KubernetesOrchestrationSpec
            {
                Exposure = OrchestrationExposureType.NodePort,
                Resources = new KubernetesResourceSpec { CpuLimit = "100m" }
            }
        }));

        Assert.Equal(OrchestrationExposureType.Ingress, service.Orchestration.Kubernetes.Exposure);
        Assert.Equal("750m", service.Orchestration.Kubernetes.Resources.CpuLimit);
        Assert.Equal("512Mi", service.Orchestration.Kubernetes.Resources.MemoryLimit);
    }

    [Fact]
    public void ComposeParser_PreservesOverridesWhenComposeFieldsAreMissing()
    {
        var yaml = """
services:
  web:
    image: nginx:alpine
    x-noctf-orchestration: |-
      {"command":"sleep infinity","exposedPort":8080,"environment":{"BASE":"1"}}
""";

        var service = Assert.Single(KubernetesComposeParser.Parse(yaml, new OrchestrationSpec()));

        Assert.Equal("sleep infinity", service.Command);
        Assert.Equal(8080, service.Orchestration.ExposedPort);
        Assert.Equal("1", service.Environment["BASE"]);
    }

    [Fact]
    public void VolumeDataFactories_CreateConfigMapAndSecretPayloads()
    {
        var configVolume = new KubernetesVolumeSpec
        {
            Name = "app-config",
            Type = "configMap",
            Data = new Dictionary<string, string> { ["config.json"] = "{}" }
        };
        var secretVolume = new KubernetesVolumeSpec
        {
            Name = "app-secret",
            Type = "secret",
            Data = new Dictionary<string, string> { ["password"] = "secret" }
        };

        var configMap = KubernetesManifestFactory.ConfigMapVolume("ns", configVolume);
        var secret = KubernetesManifestFactory.SecretVolume("ns", secretVolume);

        Assert.Equal("{}", configMap.Data["config.json"]);
        Assert.Equal("Opaque", secret.Type);
        Assert.Equal("secret", System.Text.Encoding.UTF8.GetString(secret.Data["password"]));
    }
}
