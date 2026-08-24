using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Compose;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesComposeManifestPolicyTests
{
    [Test]
    public async Task Safe_kompose_deployment_and_cluster_ip_service_are_accepted()
    {
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        await Assert.That(manifests.Deployments).Count().IsEqualTo(1);
        await Assert.That(manifests.Services).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Dangerous_workload_fields_are_rejected_after_conversion()
    {
        const string yaml = """
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: web
              namespace: attacker
              labels:
                io.kompose.service: web
            spec:
              replicas: 2
              selector:
                matchLabels:
                  io.kompose.service: web
              template:
                metadata:
                  labels:
                    io.kompose.service: web
                spec:
                  hostNetwork: true
                  hostPID: true
                  serviceAccountName: privileged
                  volumes:
                    - name: host
                      hostPath:
                        path: /
                  containers:
                    - name: web
                      image: challenge:v1
                      securityContext:
                        privileged: true
                        capabilities:
                          add: [SYS_ADMIN]
                      ports:
                        - containerPort: 8080
                          hostPort: 8080
                      volumeMounts:
                        - name: host
                          mountPath: /host
            """;

        var action = () => KubernetesComposeManifestPolicy.ParseAndValidate(
            yaml,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("cannot set metadata.namespace");
        await Assert.That(exception.Message).Contains("must use exactly one replica");
        await Assert.That(exception.Message).Contains("cannot use host namespaces");
        await Assert.That(exception.Message).Contains("cannot use volumes");
        await Assert.That(exception.Message).Contains("cannot use a service account");
        await Assert.That(exception.Message).Contains("cannot run privileged");
        await Assert.That(exception.Message).Contains("cannot add capabilities");
        await Assert.That(exception.Message).Contains("cannot publish host ports");
        await Assert.That(exception.Message).Contains("cannot mount volumes");
    }

    [Test]
    public async Task Node_port_and_persistent_volume_claim_are_rejected()
    {
        const string yaml = """
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: web
              labels:
                io.kompose.service: web
            spec:
              replicas: 1
              selector:
                matchLabels:
                  io.kompose.service: web
              template:
                metadata:
                  labels:
                    io.kompose.service: web
                spec:
                  containers:
                    - name: web
                      image: challenge:v1
            ---
            apiVersion: v1
            kind: Service
            metadata:
              name: web
              labels:
                io.kompose.service: web
            spec:
              type: NodePort
              selector:
                io.kompose.service: web
              ports:
                - port: 8080
                  targetPort: 8080
                  nodePort: 32000
            ---
            apiVersion: v1
            kind: PersistentVolumeClaim
            metadata:
              name: data
            spec:
              accessModes: [ReadWriteOnce]
              resources:
                requests:
                  storage: 1Gi
            """;

        var action = () => KubernetesComposeManifestPolicy.ParseAndValidate(
            yaml,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("cannot use Service type 'NodePort'");
        await Assert.That(exception.Message).Contains("PersistentVolumeClaim");
    }

    [Test]
    public async Task Converted_deployments_must_match_the_compose_service_set()
    {
        var action = () => KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web", "worker"], StringComparer.Ordinal));

        var exception = await Assert.That(action).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message)
            .Contains("Compose service 'worker' did not produce one Deployment");
    }

    [Test]
    public async Task Platform_policy_injects_runtime_DNS_resources_and_isolation()
    {
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var plan = KubernetesComposeManifestPolicy.ApplyPlatformPolicy(
            manifests,
            Request(),
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PodPidsLimit: 512,
                ClusterDomain: "internal.example",
                ClusterDnsServiceAddress: "10.96.0.10",
                NetworkPolicyRequired: true));

        await Assert.That(plan.RuntimeName)
            .IsEqualTo("rt-11111111111111111111111111111111");
        var headless = plan.Services.Single(service =>
            service.Spec.ClusterIP == "None");
        await Assert.That(headless.Metadata.Name).IsEqualTo(plan.RuntimeName);
        await Assert.That(headless.Spec.PublishNotReadyAddresses).IsTrue();
        await Assert.That(headless.Spec.Selector["noctf.io/runtime-instance-id"])
            .IsEqualTo("11111111-1111-1111-1111-111111111111");

        var deployment = plan.Deployments.Single();
        await Assert.That(deployment.Spec.Replicas).IsEqualTo(1);
        await Assert.That(deployment.Spec.Template.Spec.Hostname).IsEqualTo("web");
        await Assert.That(deployment.Spec.Template.Spec.Subdomain).IsEqualTo(plan.RuntimeName);
        await Assert.That(deployment.Spec.Template.Spec.DnsPolicy).IsEqualTo("ClusterFirst");
        await Assert.That(deployment.Spec.Template.Spec.DnsConfig.Searches.Single())
            .IsEqualTo($"{plan.RuntimeName}.runtime.svc.internal.example");
        await Assert.That(deployment.Spec.Template.Spec.AutomountServiceAccountToken).IsFalse();
        await Assert.That(deployment.Spec.Template.Spec.NodeSelector[
                KubernetesRuntimeOptions.PodPidsLimitNodeLabel])
            .IsEqualTo("512");
        var container = deployment.Spec.Template.Spec.Containers.Single();
        await Assert.That(container.SecurityContext!.AllowPrivilegeEscalation).IsFalse();
        await Assert.That(container.SecurityContext.Capabilities!.Drop).IsEquivalentTo(["ALL"]);
        await Assert.That(container.Resources!.Limits!["cpu"].ToString()).IsEqualTo("500m");
        await Assert.That(container.Resources.Limits["memory"].ToString())
            .IsEqualTo("268435456");

        var publicService = plan.Services.Single(service =>
            service.Spec.Type == "NodePort");
        await Assert.That(publicService.Spec.Ports.Single().NodePort).IsNull();
        await Assert.That(publicService.Spec.Ports.Single().TargetPort.Value)
            .IsEqualTo("8080");
        await Assert.That(plan.NetworkPolicy.Spec.PolicyTypes)
            .IsEquivalentTo(["Ingress", "Egress"]);
        await Assert.That(plan.NetworkPolicy.Spec.Egress).Count().IsEqualTo(2);
        await Assert.That(plan.NetworkPolicy.Spec.Egress.Single(rule =>
                rule.Ports?.Any(port => port.Port.Value == "53") == true).Ports)
            .Count().IsEqualTo(2);
        var dns = plan.NetworkPolicy.Spec.Egress.Single(rule =>
            rule.Ports?.Any(port => port.Port.Value == "53") == true);
        await Assert.That(dns.To.Single(peer => peer.IpBlock is not null).IpBlock!.Cidr)
            .IsEqualTo("10.96.0.10/32");
    }

    [Test]
    public async Task Platform_policy_refuses_a_pool_without_network_policy_enforcement()
    {
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var action = () => KubernetesComposeManifestPolicy.ApplyPlatformPolicy(
            manifests,
            Request(),
            new KubernetesRuntimeOptions(NetworkPolicyRequired: false));

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("requires NetworkPolicy enforcement");
    }

    [Test]
    public async Task Platform_policy_refuses_a_pool_without_cluster_DNS_address()
    {
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var action = () => KubernetesComposeManifestPolicy.ApplyPlatformPolicy(
            manifests,
            Request(),
            new KubernetesRuntimeOptions(
                PodPidsLimit: 512,
                ClusterDomain: "cluster.local"));

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("ClusterDnsServiceAddress");
    }

    [Test]
    public async Task InternetOnly_allows_public_IPv4_except_built_in_and_pool_ranges()
    {
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var plan = KubernetesComposeManifestPolicy.ApplyPlatformPolicy(
            manifests,
            Request() with { EgressPolicy = RuntimeEgressPolicy.InternetOnly },
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PodPidsLimit: 512,
                ClusterDomain: "cluster.local",
                ClusterDnsServiceAddress: "10.96.0.10",
                NetworkPolicyRequired: true,
                ProtectedCidrs: ["172.30.0.0/16"]));

        await Assert.That(plan.NetworkPolicy.Spec.Egress).Count().IsEqualTo(3);
        var internet = plan.NetworkPolicy.Spec.Egress.Single(rule =>
            rule.To?.Any(peer => peer.IpBlock?.Cidr == "0.0.0.0/0") == true);
        await Assert.That(internet.To).Count().IsEqualTo(1);
        await Assert.That(internet.To.Single().IpBlock!.Cidr).IsEqualTo("0.0.0.0/0");
        await Assert.That(internet.To.Single().IpBlock!.Except)
            .Contains("172.30.0.0/16");
        await Assert.That(internet.To.Single().IpBlock!.Except)
            .Contains("169.254.0.0/16");
        await Assert.That(internet.To.Single().IpBlock!.Except.Any(cidr =>
            cidr.Contains(':'))).IsFalse();
    }

    [Test]
    public async Task InternetOnly_refuses_a_pool_without_protected_CIDRs()
    {
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            SafeManifests,
            new HashSet<string>(["web"], StringComparer.Ordinal));

        var action = () => KubernetesComposeManifestPolicy.ApplyPlatformPolicy(
            manifests,
            Request() with { EgressPolicy = RuntimeEgressPolicy.InternetOnly },
            new KubernetesRuntimeOptions(
                PodPidsLimit: 512,
                ClusterDomain: "cluster.local",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("ProtectedCidrs");
    }

    private static ComposeRequest Request() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        RuntimeProvider.Kubernetes,
        "display-project",
        "services: {}",
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["noctf.io/competition-id"] = "competition" },
        new Dictionary<string, RuntimeResourceLimits>
        {
            ["web"] = new(268_435_456, 500_000_000, 0)
        },
        new(268_435_456, 500_000_000, 0),
        TimeSpan.FromHours(1),
        TimeSpan.FromMinutes(2),
        [
            new RuntimeUrlBinding(
                "http://{HOST}:{PORT}",
                RuntimeExposure.Participants,
                ContainerPort: 8080,
                ServiceName: "web")
        ]);

    private const string SafeManifests = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: web
          labels:
            io.kompose.service: web
        spec:
          replicas: 1
          selector:
            matchLabels:
              io.kompose.service: web
          template:
            metadata:
              labels:
                io.kompose.service: web
            spec:
              containers:
                - name: web
                  image: challenge:v1
        ---
        apiVersion: v1
        kind: Service
        metadata:
          name: web
          labels:
            io.kompose.service: web
        spec:
          type: ClusterIP
          selector:
            io.kompose.service: web
          ports:
            - port: 8080
              targetPort: 8080
        """;
}
