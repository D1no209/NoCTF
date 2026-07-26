using k8s;
using k8s.Autorest;
using k8s.Models;
using NSubstitute;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesRuntimePoolStartupCheckTests
{
    [Test]
    public async Task Valid_deployment_owned_default_deny_is_accepted()
    {
        var check = CreateCheck(ValidPolicy());

        await check.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task Missing_baseline_policy_refuses_runner_startup()
    {
        var check = CreateCheck(null);

        var action = () => check.StartAsync(CancellationToken.None);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message)
            .Contains(KubernetesRuntimePoolStartupCheck.PolicyName);
    }

    [Test]
    public async Task Policy_that_allows_egress_refuses_runner_startup()
    {
        var policy = ValidPolicy();
        policy.Spec.Egress =
        [
            new V1NetworkPolicyEgressRule()
        ];
        var check = CreateCheck(policy);

        var action = () => check.StartAsync(CancellationToken.None);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("deny all ingress and egress");
    }

    [Test]
    public async Task Cilium_without_always_enforcement_refuses_runner_startup()
    {
        var check = CreateCheck(ValidPolicy(), ciliumEnforcementMode: "default");

        var action = () => check.StartAsync(CancellationToken.None);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("configured as 'always'");
    }

    [Test]
    public async Task Mismatched_cluster_DNS_address_refuses_runner_startup()
    {
        var check = CreateCheck(ValidPolicy(), kubeDnsAddress: "10.96.0.11");

        var action = () => check.StartAsync(CancellationToken.None);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("does not match Service");
    }

    private static KubernetesRuntimePoolStartupCheck CreateCheck(
        V1NetworkPolicy? policy,
        string ciliumEnforcementMode = "always",
        string kubeDnsAddress = "10.96.0.10")
    {
        var client = Substitute.For<IKubernetes>();
        var core = Substitute.For<ICoreV1Operations>();
        var networking = Substitute.For<INetworkingV1Operations>();
        client.CoreV1.Returns(core);
        client.NetworkingV1.Returns(networking);
        core.ReadNamespacedConfigMapWithHttpMessagesAsync(
                KubernetesRuntimePoolStartupCheck.CiliumConfigMapName,
                KubernetesRuntimePoolStartupCheck.CiliumConfigNamespace,
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1ConfigMap>
            {
                Body = new V1ConfigMap
                {
                    Data = new Dictionary<string, string>
                    {
                        [KubernetesRuntimePoolStartupCheck.CiliumPolicyEnforcementKey] =
                            ciliumEnforcementMode
                    }
                }
            }));
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                KubernetesRuntimePoolStartupCheck.KubeDnsServiceName,
                KubernetesRuntimePoolStartupCheck.KubeDnsServiceNamespace,
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = new V1Service
                {
                    Spec = new V1ServiceSpec
                    {
                        ClusterIP = kubeDnsAddress,
                        ClusterIPs = [kubeDnsAddress]
                    }
                }
            }));
        var call = networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
            KubernetesRuntimePoolStartupCheck.PolicyName,
            "runtime",
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
            Arg.Any<CancellationToken>());
        if (policy is null)
        {
            call.Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(
                new HttpOperationException("not found")
                {
                    Response = new HttpResponseMessageWrapper(
                        new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
                        string.Empty)
                }));
        }
        else
        {
            call.Returns(Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
            {
                Body = policy
            }));
        }
        return new KubernetesRuntimePoolStartupCheck(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                ClusterDnsServiceAddress: "10.96.0.10"));
    }

    private static V1NetworkPolicy ValidPolicy() =>
        new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = KubernetesRuntimePoolStartupCheck.PolicyName,
                NamespaceProperty = "runtime",
                Labels = new Dictionary<string, string>
                {
                    ["noctf.io/purpose"] =
                        KubernetesRuntimePoolStartupCheck.PurposeLabel
                }
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector(),
                PolicyTypes = ["Ingress", "Egress"],
                Ingress = [],
                Egress = []
            }
        };
}
