using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class KubernetesCapacityObservationTests
{
    [Test, Timeout(300_000)]
    public async Task Actual_requests_and_observation_use_the_same_attested_nodes_and_missing_permissions_block(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_CAPACITY_INTEGRATION") != "true")
        {
            Skip.Test("Requires an isolated Kubernetes cluster with metrics-server and pod-pids-limit=128 attested nodes.");
            return;
        }
        var clientConfiguration = KubernetesClientConfiguration.BuildDefaultConfig();
        using var client = new Kubernetes(clientConfiguration);
        var ns = "noctf-capacity-" + Guid.NewGuid().ToString("N");
        var roleCreated = false;
        await client.CoreV1.CreateNamespaceAsync(new V1Namespace { Metadata = new V1ObjectMeta { Name = ns } }, cancellationToken: ct);
        try
        {
            var allNodes = await client.CoreV1.ListNodeAsync(cancellationToken: ct);
            var eligible = await client.CoreV1.ListNodeAsync(labelSelector: "noctf.io/pod-pids-limit=128", cancellationToken: ct);
            await Assert.That(eligible.Items.Count).IsGreaterThan(0);
            await Assert.That(eligible.Items.Count).IsLessThan(allNodes.Items.Count);
            var options = new KubernetesRuntimeOptions(Namespace: ns, PodPidsLimit: 128);
            var lifecycle = new KubernetesContainerLifecycle(client, options);
            var id = Guid.NewGuid();
            var policy = new RuntimeResourceBudgetPolicy();
            var limits = policy.EffectiveLimit(new(64 * 1024 * 1024, 201_000_001, 128), RuntimeProvider.Kubernetes);
            var budget = policy.Calculate(limits, RuntimeProvider.Kubernetes);
            var receipt = await lifecycle.CreateAsync(new(id, RuntimeProvider.Kubernetes, "busybox:1.36.1", ["sleep", "180"], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int>(), limits, null, OperationTimeout: TimeSpan.FromMinutes(2), Budget: budget), ct);
            try
            {
                var pod = await client.CoreV1.ReadNamespacedPodAsync(receipt.ResourceId, ns, cancellationToken: ct);
                await Assert.That(eligible.Items.Select(node => node.Metadata.Name)).Contains(pod.Spec.NodeName);
                await Assert.That(pod.Spec.Containers[0].Resources.Requests["cpu"].ToDecimal()).IsEqualTo(.202m);
                await Assert.That(pod.Spec.Containers[0].Resources.Limits["cpu"].ToDecimal()).IsEqualTo(.202m);
                await Assert.That(pod.Spec.Containers[0].Resources.Requests["memory"].ToDecimal()).IsEqualTo(limits.MemoryBytes);
                await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
                await postgres.StartAsync(ct);
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    { ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString() }).Build();
                var runner = Options.Create(new RunnerOptions { Id = "kube-capacity-test", Provider = RuntimeProvider.Kubernetes });
                await using var observer = new RunnerResourceObserver(runner, new DockerRuntimeOptions(), options, client, new InMemoryClusterLeaseManager(),
                    Substitute.For<IHostApplicationLifetime>(), TimeProvider.System, NullLogger<RunnerResourceObserver>.Instance);
                RunnerAdmissionSnapshot snapshot = await observer.SampleAsync(ct);
                for (var attempt = 0; attempt < 10 && snapshot.State != RunnerAdmissionState.Ready; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    snapshot = await observer.SampleAsync(ct);
                }
                await Assert.That(snapshot.State).IsEqualTo(RunnerAdmissionState.Ready);
                await Assert.That(snapshot.Observation!.CpuMillicores).IsEqualTo(eligible.Items.Sum(node => (long)(node.Status.Allocatable["cpu"].ToDecimal() * 1_000_000_000m)));
                await Assert.That(snapshot.Observation.MemoryTotalBytes).IsEqualTo(eligible.Items.Sum(node => (long)node.Status.Allocatable["memory"].ToDecimal()));
                await Assert.That(snapshot.Observation.PidsUsed).IsNull();
                await Assert.That(snapshot.Observation.PidsCapacity).IsNull();
                await Assert.That(snapshot.Capacity!.ObservedTotal.PidsLimit).IsNull();
                await client.CoreV1.CreateNamespacedServiceAccountAsync(new V1ServiceAccount { Metadata = new V1ObjectMeta { Name = "unprivileged" } }, ns, cancellationToken: ct);
                await client.RbacAuthorizationV1.CreateClusterRoleAsync(new V1ClusterRole
                {
                    Metadata = new V1ObjectMeta { Name = ns },
                    Rules = [new V1PolicyRule { ApiGroups = [""], Resources = ["nodes", "namespaces", "pods"], Verbs = ["get", "list"] }]
                }, cancellationToken: ct);
                roleCreated = true;
                await client.RbacAuthorizationV1.CreateClusterRoleBindingAsync(new V1ClusterRoleBinding
                {
                    Metadata = new V1ObjectMeta { Name = ns }, RoleRef = new V1RoleRef { ApiGroup = "rbac.authorization.k8s.io", Kind = "ClusterRole", Name = ns },
                    Subjects = [new Rbacv1Subject { Kind = "ServiceAccount", Name = "unprivileged", NamespaceProperty = ns }]
                }, cancellationToken: ct);
                var token = await client.CoreV1.CreateNamespacedServiceAccountTokenAsync(new Authenticationv1TokenRequest
                { Spec = new V1TokenRequestSpec { Audiences = ["https://kubernetes.default.svc.cluster.local"], ExpirationSeconds = 600 } }, "unprivileged", ns, cancellationToken: ct);
                using var restricted = new Kubernetes(new KubernetesClientConfiguration
                { Host = clientConfiguration.Host, SslCaCerts = clientConfiguration.SslCaCerts, AccessToken = token.Status.Token });
                await Assert.That((await restricted.CoreV1.ListNodeAsync(cancellationToken: ct)).Items.Count).IsGreaterThan(0);
                await using var denied = new RunnerResourceObserver(runner, new DockerRuntimeOptions(), options, restricted, new InMemoryClusterLeaseManager(),
                    Substitute.For<IHostApplicationLifetime>(), TimeProvider.System, NullLogger<RunnerResourceObserver>.Instance);
                var blocked = await denied.SampleAsync(ct);
                await Assert.That(blocked.State).IsEqualTo(RunnerAdmissionState.Starting);
                await Assert.That(blocked.Failure).IsEqualTo(RunnerAdmissionFailure.ObservationStale);
                await Assert.That(blocked.Observation).IsNull();
                var output = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_MEASUREMENTS");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    await File.WriteAllTextAsync(Path.Combine(output, "kubernetes-capacity.json"), System.Text.Json.JsonSerializer.Serialize(new
                    {
                        nodeCount = allNodes.Items.Count, eligibleNodes = eligible.Items.Select(node => node.Metadata.Name).ToArray(),
                        scheduledNode = pod.Spec.NodeName, cpuRequest = pod.Spec.Containers[0].Resources.Requests["cpu"].ToDecimal(),
                        cpuLimit = pod.Spec.Containers[0].Resources.Limits["cpu"].ToDecimal(), snapshot, metricsPermissionDenied = blocked
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
                }
            }
            finally { await lifecycle.DestroyAsync(receipt, CancellationToken.None); }
        }
        finally
        {
            if (roleCreated)
            {
                await client.RbacAuthorizationV1.DeleteClusterRoleBindingAsync(ns, cancellationToken: CancellationToken.None);
                await client.RbacAuthorizationV1.DeleteClusterRoleAsync(ns, cancellationToken: CancellationToken.None);
            }
            await client.CoreV1.DeleteNamespaceAsync(ns, body: new V1DeleteOptions { PropagationPolicy = "Foreground" }, cancellationToken: CancellationToken.None);
        }
    }
}
