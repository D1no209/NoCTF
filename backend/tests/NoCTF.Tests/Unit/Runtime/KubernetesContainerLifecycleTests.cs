using k8s;
using k8s.Autorest;
using k8s.Models;
using NSubstitute;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesContainerLifecycleTests
{
    [Test]
    [Arguments(
        ContainerNetworkPurpose.AwdpVerification,
        "awdp-checker",
        "awdp-verification")]
    [Arguments(
        ContainerNetworkPurpose.AwdChecker,
        "awd-checker",
        "awd-checker")]
    public async Task Checker_callback_policy_only_allows_callback_port_and_dns(
        ContainerNetworkPurpose networkPurpose,
        string purpose,
        string jobKind)
    {
        var (client, core, networking) = CreateClient();
        V1Pod? createdPod = null;
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Do<V1Pod>(pod => createdPod = pod),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod> { Body = new V1Pod() }));
        V1NetworkPolicy? createdPolicy = null;
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Do<V1NetworkPolicy>(policy => createdPolicy = policy),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
            {
                Body = new V1NetworkPolicy()
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                CallbackPodLabelKey: "noctf.io/internal-role",
                CallbackPodLabelValue: "awdp-callback",
                ClusterDnsServiceAddress: "10.96.0.10"));

        _ = await lifecycle.CreateAsync(
            CheckerRequest() with
            {
                Labels = new Dictionary<string, string>
                {
                    ["noctf.io/purpose"] = purpose
                },
                NetworkPurpose = networkPurpose
            },
            CancellationToken.None);

        await Assert.That(createdPolicy).IsNotNull();
        await Assert.That(createdPod!.Metadata.Labels.ContainsKey("noctf.io/expires-at")).IsFalse();
        await Assert.That(createdPod.Metadata.Labels["noctf.io/job-kind"])
            .IsEqualTo(jobKind);
        await Assert.That(createdPolicy!.Spec.PodSelector.MatchLabels.All(label =>
            createdPod.Metadata.Labels.TryGetValue(label.Key, out var value)
            && string.Equals(value, label.Value, StringComparison.Ordinal))).IsTrue();
        var egress = createdPolicy.Spec.Egress;
        await Assert.That(egress).Count().IsEqualTo(2);
        var callback = egress.Single(rule => rule.To.Any(peer =>
            peer.PodSelector?.MatchLabels?.ContainsKey("noctf.io/internal-role") == true));
        await Assert.That(callback.To.Single().PodSelector!.MatchLabels!["noctf.io/internal-role"])
            .IsEqualTo("awdp-callback");
        await Assert.That(callback.Ports).Count().IsEqualTo(1);
        await Assert.That(callback.Ports.Single().Port.Value).IsEqualTo("8443");
        var dns = egress.Single(rule => rule.To.Any(peer => peer.NamespaceSelector is not null));
        await Assert.That(dns.Ports.Select(port => $"{port.Protocol}:{port.Port.Value}"))
            .IsEquivalentTo(["UDP:53", "TCP:53"]);
    }

    [Test]
    public async Task Callback_policy_failure_removes_the_created_pod()
    {
        var (client, core, networking) = CreateClient();
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<V1NetworkPolicy>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(
                new InvalidOperationException("policy failed")));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(CheckerRequest(), CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsTrue();
    }

    [Test]
    public async Task Ambiguous_pod_create_failure_still_attempts_deterministic_cleanup()
    {
        var (client, core, _) = CreateClient();
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Any<V1Pod>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Pod>>(
                new TimeoutException("response lost")));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(CheckerRequest(), CancellationToken.None);

        await Assert.That(action).Throws<TimeoutException>();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsTrue();
    }

    [Test]
    public async Task Ordinary_runtime_ttl_does_not_start_at_provider_create_time()
    {
        var (client, core, _) = CreateClient();
        V1Pod? createdPod = null;
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Do<V1Pod>(pod => createdPod = pod),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod> { Body = new V1Pod() }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        _ = await lifecycle.CreateAsync(
            CheckerRequest() with { AllowInternalCallback = false }, CancellationToken.None);

        await Assert.That(createdPod!.Metadata.Labels.ContainsKey("noctf.io/expires-at")).IsFalse();
    }

    [Test]
    public async Task Ambiguous_sandbox_policy_create_failure_attempts_deterministic_cleanup()
    {
        var (client, _, networking) = CreateClient();
        networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(NotFound()),
                Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
                {
                    Body = new V1NetworkPolicy
                    {
                        Metadata = new V1ObjectMeta
                        {
                            Labels = new Dictionary<string, string>
                            {
                                ["noctf.io/managed"] = "true",
                                ["noctf.io/runtime-instance-id"] =
                                    "019be6f7-882e-7cae-9389-898a98fbfe22",
                                ["noctf.io/generation"] = "3",
                                ["noctf.io/network-purpose"] = "awdp-verification"
                            }
                        }
                    }
                }));
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<V1NetworkPolicy>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(
                new TimeoutException("response lost")));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateIsolatedNetworkAsync(
            new ContainerNetworkPolicyRequest(
                new RuntimeResourceIdentity(
                    Guid.Parse("019be6f7-882e-7cae-9389-898a98fbfe22"), 3),
                ContainerNetworkPurpose.AwdpVerification,
                RuntimeEgressPolicy.DenyAll,
                [],
                8080),
            CancellationToken.None);

        await Assert.That(action).Throws<TimeoutException>();
        await Assert.That(networking.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedNetworkPolicyWithHttpMessagesAsync"))
            .IsTrue();
    }

    [Test]
    public async Task Persistent_runtime_policy_applies_public_ingress_and_InternetOnly_egress()
    {
        var (client, _, networking) = CreateClient();
        networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(NotFound()));
        V1NetworkPolicy? createdPolicy = null;
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Do<V1NetworkPolicy>(policy => createdPolicy = policy),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
            {
                Body = new V1NetworkPolicy()
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                ClusterDnsServiceAddress: "10.96.0.10",
                ProtectedCidrs: ["172.30.0.0/16"]));

        var name = await lifecycle.CreateIsolatedNetworkAsync(
            new ContainerNetworkPolicyRequest(
                new RuntimeResourceIdentity(
                    Guid.Parse("019be6f7-882e-7cae-9389-898a98fbfe22"), 3),
                ContainerNetworkPurpose.PersistentRuntime,
                RuntimeEgressPolicy.InternetOnly,
                [8080]),
            CancellationToken.None);

        await Assert.That(name)
            .IsEqualTo("noctf-rt-019be6f7882e7cae9389898a98fbfe22-3");
        await Assert.That(createdPolicy).IsNotNull();
        await Assert.That(createdPolicy!.Metadata.Labels.ContainsKey("noctf.io/expires-at"))
            .IsFalse();
        await Assert.That(createdPolicy.Spec.Ingress).Count().IsEqualTo(2);
        var publicIngress = createdPolicy.Spec.Ingress.Single(rule =>
            rule.FromProperty is null || rule.FromProperty.Count == 0);
        await Assert.That(publicIngress.Ports.Single().Port.Value).IsEqualTo("8080");
        await Assert.That(createdPolicy.Spec.Egress).Count().IsEqualTo(3);
        await Assert.That(createdPolicy.Spec.Egress.Single(rule =>
                rule.To.Any(peer => peer.IpBlock?.Cidr == "0.0.0.0/0"))
            .To.Single().IpBlock!.Except)
            .Contains("172.30.0.0/16");
    }

    private static (IKubernetes Client, ICoreV1Operations Core, INetworkingV1Operations Networking)
        CreateClient()
    {
        var client = Substitute.For<IKubernetes>();
        var core = Substitute.For<ICoreV1Operations>();
        var networking = Substitute.For<INetworkingV1Operations>();
        client.CoreV1.Returns(core);
        client.NetworkingV1.Returns(networking);
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Any<V1Pod>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod> { Body = new V1Pod() }));
        return (client, core, networking);
    }

    private static ContainerRequest CheckerRequest() => new(
        Guid.Parse("019be6f7-882e-7cae-9389-898a98fbfe22"),
        RuntimeProvider.Kubernetes,
        "checker:latest",
        ["/checker"],
        new Dictionary<string, string>
        {
            ["NOCTF_CALLBACK_URL"] = "https://callback.noctf.svc:8443/api/internal/v1/awdp/fix-results"
        },
        new Dictionary<string, string> { ["noctf.io/purpose"] = "awdp-checker" },
        new Dictionary<int, int>(),
        new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
        new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
        TimeSpan.FromMinutes(1),
        NetworkName: "sandbox-a",
        AllowInternalCallback: true,
        Generation: 3,
        NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);

    private static HttpOperationException NotFound() => new("not found")
    {
        Response = new HttpResponseMessageWrapper(
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound), string.Empty)
    };
}
