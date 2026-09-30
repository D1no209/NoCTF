using System.Globalization;
using k8s;
using k8s.Models;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes.Services;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class KubernetesNamedServicesIntegrationTests
{
    [Test, Timeout(600_000)]
    public async Task Real_cluster_resolves_named_services_and_single_pods_have_no_discovery_service(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_INTEGRATION") != "true")
            Skip.Test("Real Kubernetes integration requires NOCTF_KUBERNETES_INTEGRATION=true and a configured cluster.");
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        var scope = $"noctf-it-{Guid.NewGuid():N}";
        await client.CoreV1.CreateNamespaceAsync(new() { Metadata = new() { Name = scope } }, cancellationToken: ct);
        try
        {
            await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(new()
            {
                Metadata = new() { Name = "baseline-deny", NamespaceProperty = scope },
                Spec = new() { PodSelector = new(), PolicyTypes = ["Ingress", "Egress"], Ingress = [], Egress = [] }
            }, scope, cancellationToken: ct);
            var options = new KubernetesRuntimeOptions(Namespace: scope, PublicHost: Required("NOCTF_KUBERNETES_PUBLIC_HOST"),
                PodPidsLimit: long.Parse(Required("NOCTF_KUBERNETES_POD_PIDS_LIMIT"), CultureInfo.InvariantCulture),
                ClusterDomain: Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_CLUSTER_DOMAIN") ?? "cluster.local",
                ClusterDnsServiceAddress: Required("NOCTF_KUBERNETES_CLUSTER_DNS"), ProtectedCidrs: ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"]);
            var containers = new KubernetesContainerLifecycle(client, options);
            var runtime = new KubernetesContainerRuntime(client, containers, options);
            var single = await runtime.UpAsync(Request([new("main", "nginx:alpine")], options), ct);
            var group = await runtime.UpAsync(Request([new("web", "nginx:alpine"), new("db", "alpine:latest", Command: ["sleep"], Arguments: ["300"])], options), ct);
            await Assert.That(single.DiscoveryServiceName).IsNull();
            await Assert.That(group.DiscoveryServiceName).IsNotNull();
            var services = await client.CoreV1.ListNamespacedServiceAsync(scope, cancellationToken: ct);
            await Assert.That(services.Items.Count(service => service.Spec.ClusterIP == "None")).IsEqualTo(1);
            var check = await runtime.ExecAsync(group, "web", ["sh", "-c", $"getent hosts db | grep -F '{group.Services[1].InternalHost}'"], TimeSpan.FromSeconds(10), ct);
            await Assert.That(check.ExitCode).IsEqualTo(0);
            using var http = new HttpClient();
            foreach (var receipt in new[] { single, group })
            {
                using var response = await http.GetAsync($"http://{options.PublicHost}:{receipt.Services[0].PublishedPorts[80]}/", ct);
                await Assert.That(response.IsSuccessStatusCode).IsTrue();
                await runtime.DownAsync(receipt, ct);
            }
            await Assert.That((await client.CoreV1.ListNamespacedPodAsync(scope, cancellationToken: ct)).Items).IsEmpty();
            await Assert.That((await client.CoreV1.ListNamespacedServiceAsync(scope, cancellationToken: ct)).Items).IsEmpty();
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await client.CoreV1.DeleteNamespaceAsync(scope, cancellationToken: cleanup.Token);
        }
    }

    private static ContainerRuntimeRequest Request(RuntimeServiceDefinition[] services, KubernetesRuntimeOptions options) => new(Guid.NewGuid(), RuntimeProvider.Kubernetes,
        services, new Dictionary<string, string>(), RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(options.PodPidsLimit))),
        null, TimeSpan.FromMinutes(3), [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, services[0].Name)]);
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required for cluster integration.");
}
