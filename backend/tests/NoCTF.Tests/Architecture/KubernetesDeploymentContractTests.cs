using YamlDotNet.RepresentationModel;

namespace NoCTF.Tests.Architecture;

public sealed class KubernetesDeploymentContractTests
{
    [Test]
    [Arguments("backend-deployment.yaml", "Api")]
    [Arguments("worker-deployment.yaml", "Worker")]
    [Arguments("runner-deployment.yaml", "Runner")]
    public async Task Each_role_explicitly_selects_the_unified_host_and_platform_nodes(string file, string role)
    {
        var workload = Documents("base", file).Single();
        var pod = At(workload, "spec", "template", "spec");
        var container = ((YamlSequenceNode)At(pod, "containers")).Children.Single();
        await Assert.That(Text(At(container, "image"))).IsEqualTo("noctf-host:dev");
        var roles = ((YamlSequenceNode)At(container, "env")).Children
            .Where(item => Text(At(item, "name")).StartsWith("Hosting__Roles__", StringComparison.Ordinal)).ToArray();
        await Assert.That(roles.Length).IsEqualTo(1);
        await Assert.That(Text(At(roles.Single(), "value"))).IsEqualTo(role);
        await Assert.That(Text(At(pod, "nodeSelector", "noctf.io/node-role"))).IsEqualTo("platform");
        if (role == "Runner") await Assert.That(Text(At(workload, "spec", "replicas"))).IsEqualTo("1");
    }

    [Test]
    public async Task Ordinary_updates_exclude_initialization_and_secret_replacement()
    {
        var configuration = Documents("base", "kustomization.yaml").Single();
        var resources = ((YamlSequenceNode)At(configuration, "resources")).Children.Select(Text).ToArray();
        foreach (var resource in resources)
        {
        if (Directory.Exists(Path.Combine(Root(), "deploy", "k8s", "base", resource))) continue;
        foreach (var document in Documents("base", resource))
        {
            var kind = Text(At(document, "kind"));
            await Assert.That(kind is "Job" or "Secret").IsFalse();
        }
        }
        var init = Documents("init", "base", "kustomization.yaml").Single();
        foreach (var resource in ((YamlSequenceNode)At(init, "resources")).Children.Select(Text))
            await Assert.That(Text(At(Documents("init", "base", resource).Single(), "kind"))).IsEqualTo("Job");
    }

    [Test]
    public async Task Rustfs_uses_its_own_persistent_volume_and_private_console_with_secret_credentials()
    {
        var storage = Documents("base", "rustfs-deployment.yaml").Single();
        var pod = At(storage, "spec", "template", "spec");
        var container = ((YamlSequenceNode)At(pod, "containers")).Children.Single();
        await Assert.That(Text(At(container, "image"))).StartsWith("rustfs/rustfs:1.0.0@sha256:");
        await Assert.That(Text(At(pod, "securityContext", "runAsUser"))).IsEqualTo("10001");
        foreach (var credential in ((YamlSequenceNode)At(container, "env")).Children
                     .Where(item => Text(At(item, "name")) is "RUSTFS_ACCESS_KEY" or "RUSTFS_SECRET_KEY"))
            await Assert.That(Text(At(credential, "valueFrom", "secretKeyRef", "name"))).IsEqualTo("noctf-secrets");
        var service = Documents("base", "rustfs-service.yaml").Single();
        await Assert.That(Text(At(service, "spec", "type"))).IsEqualTo("ClusterIP");
        var volume = ((YamlSequenceNode)At(pod, "volumes")).Children.Single(item => Text(At(item, "name")) == "rustfs-data");
        await Assert.That(Text(At(volume, "persistentVolumeClaim", "claimName"))).IsEqualTo("rustfs-pvc");
    }

    [Test]
    public async Task Non_runtime_allow_policy_cannot_override_platform_default_deny()
    {
        var path = Path.Combine(Root(), "deploy", "k8s", "platform", "cilium", "non-runtime-allow.yaml");
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var expressions = (YamlSequenceNode)At(yaml.Documents.Single().RootNode, "spec", "endpointSelector", "matchExpressions");
        var purpose = expressions.Children.Single(item => Text(At(item, "key")) == "io.cilium.k8s.namespace.labels.noctf.io/purpose");
        await Assert.That(Text(At(purpose, "operator"))).IsEqualTo("NotIn");
        await Assert.That(((YamlSequenceNode)At(purpose, "values")).Children.Select(Text)).IsEquivalentTo(["challenge-runtime", "platform"]);
    }

    private static YamlNode At(YamlNode node, params string[] keys)
    {
        foreach (var key in keys) node = ((YamlMappingNode)node).Children[new YamlScalarNode(key)];
        return node;
    }
    private static string Text(YamlNode node) => ((YamlScalarNode)node).Value ?? string.Empty;
    private static IEnumerable<YamlNode> Documents(params string[] parts)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine([Root(), "deploy", "k8s", .. parts]))));
        return yaml.Documents.Select(document => document.RootNode);
    }
    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "AGENTS.md"))) path = path.Parent;
        return path?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
