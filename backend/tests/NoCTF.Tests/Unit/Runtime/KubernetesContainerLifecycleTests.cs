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
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = new V1Pod
                {
                    Metadata = new V1ObjectMeta { Uid = "pod-uid" }
                }
            }));
        V1NetworkPolicy? createdPolicy = null;
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Do<V1NetworkPolicy>(policy => createdPolicy = policy),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var policy = call.Arg<V1NetworkPolicy>()!;
                policy.Metadata.Uid = "policy-uid";
                return Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
                {
                    Body = policy
                });
            });
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                CallbackPodLabelKey: "noctf.io/internal-role",
                CallbackPodLabelValue: "awdp-callback",
                ClusterDnsServiceAddress: "10.96.0.10"));

        _ = await lifecycle.CreateAsync(
            CheckerRequest() with
            {
                NetworkPurpose = networkPurpose
            },
            CancellationToken.None);

        await Assert.That(createdPolicy).IsNotNull();
        await Assert.That(createdPod!.Metadata.Labels.ContainsKey("noctf.io/expires-at")).IsFalse();
        await Assert.That(createdPod.Metadata.Labels["noctf.io/job-kind"])
            .IsEqualTo(jobKind);
        await Assert.That(createdPod.Metadata.Labels["noctf.io/purpose"])
            .IsEqualTo(purpose);
        await Assert.That(createdPolicy!.Spec.PodSelector.MatchLabels.All(label =>
            createdPod.Metadata.Labels.TryGetValue(label.Key, out var value)
            && string.Equals(value, label.Value, StringComparison.Ordinal))).IsTrue();
        var egress = createdPolicy.Spec.Egress;
        await Assert.That(egress).Count().IsEqualTo(2);
        var callback = egress.Single(rule => rule.To.Any(peer =>
            peer.PodSelector?.MatchLabels?.ContainsKey("noctf.io/internal-role") == true));
        await Assert.That(callback.To.Single().PodSelector!.MatchLabels!["noctf.io/internal-role"])
            .IsEqualTo("awdp-callback");
        await Assert.That(callback.To.Single().NamespaceSelector!.MatchLabels![
                "kubernetes.io/metadata.name"])
            .IsEqualTo("noctf");
        await Assert.That(callback.Ports).Count().IsEqualTo(1);
        await Assert.That(callback.Ports.Single().Port.Value).IsEqualTo("8443");
        var dns = egress.Single(rule => rule.To.Any(peer =>
            peer.NamespaceSelector?.MatchLabels?.TryGetValue(
                "kubernetes.io/metadata.name",
                out var namespaceName) == true
            && namespaceName == "kube-system"));
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
    public async Task Callback_policy_create_response_loss_reads_back_owned_policy()
    {
        var (client, core, networking) = CreateClient();
        V1NetworkPolicy? createdPolicy = null;
        networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => createdPolicy is null
                ? Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(NotFound())
                : Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
                {
                    Body = createdPolicy
                }));
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Do<V1NetworkPolicy>(policy =>
                {
                    createdPolicy = policy;
                    policy.Metadata.Uid = "policy-uid";
                }),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(
                new TimeoutException("response lost")));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                CallbackPodLabelKey: "noctf.io/internal-role",
                CallbackPodLabelValue: "awdp-callback",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.CreateAsync(CheckerRequest(), CancellationToken.None);

        await Assert.That(receipt.Status).IsEqualTo(RuntimeStatus.Pending);
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsFalse();
        await Assert.That(networking.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name
                == "DeleteNamespacedNetworkPolicyWithHttpMessagesAsync")).IsFalse();
    }

    [Test]
    [Arguments(CallbackPolicyDrift.EndPort)]
    [Arguments(CallbackPolicyDrift.MatchExpression)]
    [Arguments(CallbackPolicyDrift.CallbackNamespace)]
    public async Task Callback_policy_replay_rejects_identity_or_port_range_drift(
        CallbackPolicyDrift drift)
    {
        var (client, _, networking) = CreateClient();
        var request = CheckerRequest();
        var policy = ExistingCallbackPolicy(
            request,
            endPort: drift == CallbackPolicyDrift.EndPort ? 65535 : null);
        if (drift == CallbackPolicyDrift.MatchExpression)
        {
            policy.Spec.PodSelector.MatchExpressions =
            [
                new V1LabelSelectorRequirement
                {
                    Key = "noctf.io/runtime-id",
                    OperatorProperty = "DoesNotExist"
                }
            ];
        }
        if (drift == CallbackPolicyDrift.CallbackNamespace)
        {
            policy.Spec.Egress.Single(rule => rule.Ports.Count == 1)
                .To.Single().NamespaceSelector!.MatchLabels!["kubernetes.io/metadata.name"] =
                "other";
        }
        networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
            {
                Body = policy
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                CallbackPodLabelKey: "noctf.io/internal-role",
                CallbackPodLabelValue: "awdp-callback",
                ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(request, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(networking.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name
                == "CreateNamespacedNetworkPolicyWithHttpMessagesAsync")).IsFalse();
    }

    [Test]
    public async Task Created_callback_policy_drift_is_cleaned_with_uid_precondition()
    {
        var (client, _, networking) = CreateClient();
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<V1NetworkPolicy>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var policy = call.Arg<V1NetworkPolicy>()!;
                policy.Metadata.Uid = "policy-uid";
                policy.Spec.Egress.Single(rule => rule.Ports.Count == 1)
                    .Ports.Single().EndPort = 65535;
                return Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
                {
                    Body = policy
                });
            });
        V1DeleteOptions? policyDelete = null;
        networking.DeleteNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Do<V1DeleteOptions>(delete => policyDelete = delete),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Status>
            {
                Body = new V1Status()
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(
            CheckerRequest(), CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(policyDelete!.Preconditions!.Uid).IsEqualTo("policy-uid");
    }

    [Test]
    public async Task Ambiguous_pod_create_failure_does_not_delete_unverified_resource()
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
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsFalse();
    }

    [Test]
    public async Task Callback_pod_readback_rejects_checker_purpose_drift()
    {
        var (client, core, networking) = CreateClient();
        var request = CheckerRequest();
        var name = $"noctf-{request.OperationId:N}";
        var pod = ExistingPod(request, name);
        pod.Metadata.Labels["noctf.io/job-kind"] = "awdp-verification";
        pod.Metadata.Labels["noctf.io/purpose"] = "awd-checker";
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Any<V1Pod>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Pod>>(
                new TimeoutException("response lost")));
        core.ReadNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod> { Body = pod }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(request, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(networking.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name
                == "CreateNamespacedNetworkPolicyWithHttpMessagesAsync")).IsFalse();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsFalse();
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
            new KubernetesRuntimeOptions(
                PodPidsLimit: 512,
                ClusterDnsServiceAddress: "10.96.0.10"));

        _ = await lifecycle.CreateAsync(
            CheckerRequest() with { AllowInternalCallback = false }, CancellationToken.None);

        await Assert.That(createdPod!.Metadata.Labels.ContainsKey("noctf.io/expires-at")).IsFalse();
        await Assert.That(createdPod.Spec.AutomountServiceAccountToken).IsFalse();
        await Assert.That(createdPod.Spec.EnableServiceLinks).IsFalse();
        await Assert.That(createdPod.Spec.ServiceAccountName).IsNull();
        await Assert.That(createdPod.Spec.NodeSelector[
                KubernetesRuntimeOptions.PodPidsLimitNodeLabel])
            .IsEqualTo("512");
    }

    [Test]
    public async Task Persistent_runtime_creates_internal_and_public_services_with_dynamic_node_port()
    {
        var (client, core, _) = CreateClient();
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Service>>(NotFound()));
        var createdServices = new List<V1Service>();
        int? requestedNodePort = null;
        core.CreateNamespacedServiceWithHttpMessagesAsync(
                Arg.Do<V1Service>(service =>
                {
                    createdServices.Add(service);
                    if (service.Spec.Type == "NodePort")
                        requestedNodePort = service.Spec.Ports.Single().NodePort;
                }),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var service = call.Arg<V1Service>()!;
                if (service.Spec.Type == "NodePort")
                    service.Spec.Ports.Single().NodePort = 31234;
                else
                    service.Spec.ClusterIP = "10.96.0.42";
                return Task.FromResult(new HttpOperationResponse<V1Service> { Body = service });
            });
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PublicHost: "node.example",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.CreateAsync(
            PersistentRequest(), CancellationToken.None);

        await Assert.That(createdServices).Count().IsEqualTo(2);
        var internalService = createdServices.Single(service => service.Spec.Type == "ClusterIP");
        var publicService = createdServices.Single(service => service.Spec.Type == "NodePort");
        var resourceName = $"noctf-{PersistentRequest().OperationId:N}";
        await Assert.That(internalService.Metadata.Name).IsEqualTo(resourceName);
        await Assert.That(publicService.Metadata.Name).IsEqualTo($"{resourceName}-public");
        await Assert.That(internalService.Spec.Ports.Select(port => port.Port))
            .IsEquivalentTo([8080, 9090]);
        await Assert.That(publicService.Spec.Ports.Select(port => port.Port))
            .IsEquivalentTo([8080]);
        await Assert.That(internalService.Spec.Selector["noctf.io/runtime-id"])
            .IsEqualTo(resourceName);
        await Assert.That(publicService.Spec.Selector["noctf.io/runtime-id"])
            .IsEqualTo(resourceName);
        await Assert.That(publicService.Spec.Ports.Single().TargetPort.Value)
            .IsEqualTo("8080");
        await Assert.That(internalService.Metadata.Labels["noctf.io/resource-role"])
            .IsEqualTo("dns");
        await Assert.That(publicService.Metadata.Labels["noctf.io/resource-role"])
            .IsEqualTo("public");
        await Assert.That(requestedNodePort).IsNull();
        await Assert.That(receipt.PortMappings[8080]).IsEqualTo(31234);
        await Assert.That(receipt.InternalHost).IsEqualTo("10.96.0.42");
        await Assert.That(receipt.PublicHost).IsEqualTo("node.example");
    }

    [Test]
    public async Task Persistent_runtime_rejects_fixed_kubernetes_host_port_before_pod_create()
    {
        var (client, core, _) = CreateClient();
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(
            PersistentRequest(hostPort: 31000), CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "CreateNamespacedPodWithHttpMessagesAsync")).IsFalse();
    }

    [Test]
    public async Task Persistent_runtime_rejects_stale_public_service_before_pod_create()
    {
        var (client, core, _) = CreateClient();
        var request = PersistentRequest() with
        {
            PortMappings = new Dictionary<int, int>()
        };
        var name = $"noctf-{request.OperationId:N}";
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                $"{name}-public", Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = new V1Service()
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(request, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "CreateNamespacedPodWithHttpMessagesAsync")).IsFalse();
    }

    [Test]
    public async Task Persistent_runtime_reads_back_owned_service_after_create_response_loss()
    {
        var (client, core, _) = CreateClient();
        var services = new Dictionary<string, V1Service>(StringComparer.Ordinal);
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                return services.TryGetValue(name, out var service)
                    ? Task.FromResult(new HttpOperationResponse<V1Service> { Body = service })
                    : Task.FromException<HttpOperationResponse<V1Service>>(NotFound());
            });
        core.CreateNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<V1Service>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var service = call.Arg<V1Service>()!;
                if (service.Spec.Type == "NodePort")
                    service.Spec.Ports.Single().NodePort = 31234;
                else
                    service.Spec.ClusterIP = "10.96.0.42";
                services[service.Metadata.Name] = service;
                return service.Spec.Type == "NodePort"
                    ? Task.FromException<HttpOperationResponse<V1Service>>(
                        new TimeoutException("response lost"))
                    : Task.FromResult(
                        new HttpOperationResponse<V1Service> { Body = service });
            });
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PublicHost: "node.example",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.CreateAsync(
            PersistentRequest(), CancellationToken.None);

        await Assert.That(receipt.PortMappings[8080]).IsEqualTo(31234);
        await Assert.That(receipt.InternalHost).IsEqualTo("10.96.0.42");
        await Assert.That(core.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == "CreateNamespacedServiceWithHttpMessagesAsync"))
            .IsEqualTo(2);
    }

    [Test]
    public async Task Concurrent_pod_create_response_loss_reuses_owned_runtime_without_cleanup()
    {
        var (client, core, _) = CreateClient();
        var request = PersistentRequest();
        var name = $"noctf-{request.OperationId:N}";
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Any<V1Pod>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Pod>>(
                new TimeoutException("response lost")));
        core.ReadNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = ExistingPod(request, name)
            }));
        var internalService = ExistingService(request, name, "dns", "ClusterIP");
        internalService.Spec.ClusterIP = "10.96.0.42";
        var publicService = ExistingService(request, name, "public", "NodePort");
        publicService.Metadata.Name = $"{name}-public";
        publicService.Spec.Ports = publicService.Spec.Ports
            .Where(port => port.Port == 8080)
            .ToList();
        publicService.Spec.Ports.Single().NodePort = 31234;
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = call.ArgAt<string>(0) == name ? internalService : publicService
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PublicHost: "node.example",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.CreateAsync(request, CancellationToken.None);

        await Assert.That(receipt.PortMappings[8080]).IsEqualTo(31234);
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsFalse();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedServiceWithHttpMessagesAsync")).IsFalse();
    }

    [Test]
    public async Task Service_identity_drift_cleanup_does_not_delete_unowned_service()
    {
        var (client, core, _) = CreateClient();
        V1DeleteOptions? podDelete = null;
        core.DeleteNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Do<V1DeleteOptions>(delete => podDelete = delete),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = new V1Pod()
            }));
        var request = PersistentRequest();
        var name = $"noctf-{request.OperationId:N}";
        var foreignService = ExistingService(request, name, "dns", "ClusterIP");
        foreignService.Spec.ClusterIP = "10.96.0.42";
        foreignService.Metadata.Labels["noctf.io/generation"] = "2";
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = foreignService
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.CreateAsync(request, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedServiceWithHttpMessagesAsync")).IsFalse();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedPodWithHttpMessagesAsync")).IsTrue();
        await Assert.That(podDelete!.Preconditions!.Uid).IsEqualTo("pod-uid");
    }

    [Test]
    public async Task Replay_removes_owned_public_service_when_port_contract_is_empty()
    {
        var (client, core, _) = CreateClient();
        var request = PersistentRequest() with
        {
            PortMappings = new Dictionary<int, int>()
        };
        var name = $"noctf-{request.OperationId:N}";
        var internalService = ExistingService(request, name, "dns", "ClusterIP");
        internalService.Spec.ClusterIP = "10.96.0.42";
        var publicService = ExistingService(request, name, "public", "NodePort");
        publicService.Metadata.Name = $"{name}-public";
        publicService.Spec.Ports.Single().NodePort = 31234;
        var publicDeleted = false;
        V1DeleteOptions? publicDelete = null;
        core.ReadNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = ExistingPod(request, name)
            }));
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var serviceName = call.ArgAt<string>(0);
                if (serviceName == name)
                    return Task.FromResult(
                        new HttpOperationResponse<V1Service> { Body = internalService });
                return publicDeleted
                    ? Task.FromException<HttpOperationResponse<V1Service>>(NotFound())
                    : Task.FromResult(
                        new HttpOperationResponse<V1Service> { Body = publicService });
            });
        core.DeleteNamespacedServiceWithHttpMessagesAsync(
                $"{name}-public", Arg.Any<string>(),
                Arg.Do<V1DeleteOptions>(delete => publicDelete = delete),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                publicDeleted = true;
                return Task.FromResult(new HttpOperationResponse<V1Service>
                {
                    Body = new V1Service()
                });
            });
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.EnsureRunningAsync(request, CancellationToken.None);

        await Assert.That(publicDeleted).IsTrue();
        await Assert.That(publicDelete!.Preconditions!.Uid)
            .IsEqualTo("service-public-uid");
        await Assert.That(receipt.PortMappings).IsEmpty();
        await Assert.That(receipt.InternalHost).IsEqualTo("10.96.0.42");
    }

    [Test]
    public async Task Zero_port_runtime_replays_the_identity_written_during_create()
    {
        var (client, core, _) = CreateClient();
        V1Pod? createdPod = null;
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Service>>(NotFound()));
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Do<V1Pod>(pod => createdPod = pod),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = new V1Pod()
            }));
        var request = CheckerRequest() with
        {
            NetworkName = null,
            AllowInternalCallback = false,
            NetworkPurpose = ContainerNetworkPurpose.PersistentRuntime
        };
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));
        _ = await lifecycle.CreateAsync(request, CancellationToken.None);
        createdPod!.Status = new V1PodStatus { Phase = "Running" };
        core.ReadNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = createdPod
            }));

        var receipt = await lifecycle.EnsureRunningAsync(request, CancellationToken.None);

        await Assert.That(receipt.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(createdPod.Metadata.Labels["noctf.io/job-kind"])
            .IsEqualTo("persistent-runtime");
    }

    [Test]
    public async Task Awdp_target_creates_only_internal_service()
    {
        var (client, core, _) = CreateClient();
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Service>>(NotFound()));
        var createdServices = new List<V1Service>();
        core.CreateNamespacedServiceWithHttpMessagesAsync(
                Arg.Do<V1Service>(createdServices.Add),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var service = call.Arg<V1Service>()!;
                service.Spec.ClusterIP = "10.96.0.43";
                return Task.FromResult(new HttpOperationResponse<V1Service> { Body = service });
            });
        var request = PersistentRequest() with
        {
            PortMappings = new Dictionary<int, int>(),
            NetworkPurpose = ContainerNetworkPurpose.AwdpVerification
        };
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.CreateAsync(request, CancellationToken.None);

        await Assert.That(createdServices).Count().IsEqualTo(1);
        await Assert.That(createdServices.Single().Spec.Type).IsEqualTo("ClusterIP");
        await Assert.That(createdServices.Single().Metadata.Labels["noctf.io/job-kind"])
            .IsEqualTo("awdp-verification");
        await Assert.That(receipt.PortMappings).IsEmpty();
        await Assert.That(receipt.InternalHost).IsEqualTo("10.96.0.43");
    }

    [Test]
    public async Task Persistent_runtime_replay_reuses_owned_services_and_assigned_node_port()
    {
        var (client, core, _) = CreateClient();
        var request = PersistentRequest();
        var name = $"noctf-{request.OperationId:N}";
        var internalService = ExistingService(request, name, "dns", "ClusterIP");
        internalService.Spec.ClusterIP = "10.96.0.42";
        var publicService = ExistingService(request, name, "public", "NodePort");
        publicService.Metadata.Name = $"{name}-public";
        publicService.Spec.Ports = publicService.Spec.Ports
            .Where(port => port.Port == 8080)
            .ToList();
        publicService.Spec.Ports.Single().NodePort = 31234;
        core.ReadNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = ExistingPod(request, name)
            }));
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = call.ArgAt<string>(0) == name ? internalService : publicService
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PublicHost: "node.example",
                ClusterDnsServiceAddress: "10.96.0.10"));

        var receipt = await lifecycle.EnsureRunningAsync(request, CancellationToken.None);

        await Assert.That(receipt.PortMappings[8080]).IsEqualTo(31234);
        await Assert.That(receipt.InternalHost).IsEqualTo("10.96.0.42");
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "CreateNamespacedServiceWithHttpMessagesAsync"))
            .IsFalse();
    }

    [Test]
    public async Task Persistent_runtime_replay_rejects_public_service_identity_drift()
    {
        var (client, core, _) = CreateClient();
        var request = PersistentRequest();
        var name = $"noctf-{request.OperationId:N}";
        var internalService = ExistingService(request, name, "dns", "ClusterIP");
        internalService.Spec.ClusterIP = "10.96.0.42";
        var publicService = ExistingService(request, name, "public", "NodePort");
        publicService.Metadata.Name = $"{name}-public";
        publicService.Metadata.Labels["noctf.io/generation"] = "2";
        publicService.Spec.Ports = publicService.Spec.Ports
            .Where(port => port.Port == 8080)
            .ToList();
        publicService.Spec.Ports.Single().NodePort = 31234;
        core.ReadNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = ExistingPod(request, name)
            }));
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = call.ArgAt<string>(0) == name ? internalService : publicService
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                ClusterDnsServiceAddress: "10.96.0.10"));

        Func<Task> action = () => lifecycle.EnsureRunningAsync(
            request, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(core.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "CreateNamespacedServiceWithHttpMessagesAsync"))
            .IsFalse();
    }

    [Test]
    public async Task Destroy_removes_internal_and_public_services()
    {
        var (client, core, networking) = CreateClient();
        networking.DeleteNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Status>
            {
                Body = new V1Status()
            }));
        core.DeleteNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = new V1Service()
            }));
        core.DeleteNamespacedPodWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = new V1Pod()
            }));
        var lifecycle = new KubernetesContainerLifecycle(
            client,
            new KubernetesRuntimeOptions(ClusterDnsServiceAddress: "10.96.0.10"));
        const string resourceId = "noctf-019be6f7882e7cae9389898a98fbfe22";

        await lifecycle.DestroyAsync(
            new ContainerReceipt(
                Guid.Parse("019be6f7-882e-7cae-9389-898a98fbfe22"),
                RuntimeProvider.Kubernetes,
                resourceId,
                RuntimeStatus.Running,
                new Dictionary<int, int> { [8080] = 31234 },
                "node.example",
                "10.96.0.42"),
            CancellationToken.None);

        var deletedServices = core.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name
                == "DeleteNamespacedServiceWithHttpMessagesAsync")
            .Select(call => (string)call.GetArguments()[0]!)
            .ToArray();
        await Assert.That(deletedServices)
            .IsEquivalentTo([resourceId, $"{resourceId}-public"]);
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
                RuntimeEgressPolicy.Isolated,
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
        networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(NotFound()));
        core.CreateNamespacedPodWithHttpMessagesAsync(
                Arg.Any<V1Pod>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Pod>
            {
                Body = new V1Pod
                {
                    Metadata = new V1ObjectMeta { Uid = "pod-uid" }
                }
            }));
        return (client, core, networking);
    }

    private static ContainerRequest PersistentRequest(int hostPort = 0)
    {
        var runtimeId = Guid.Parse("019be6f7-882e-7cae-9389-898a98fbfe22");
        return new ContainerRequest(
            runtimeId,
            RuntimeProvider.Kubernetes,
            "challenge:latest",
            ["/challenge"],
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/job-kind"] = "persistent-runtime",
                ["noctf.io/runtime-instance-id"] = runtimeId.ToString("D"),
                ["noctf.io/generation"] = "3"
            },
            new Dictionary<int, int> { [8080] = hostPort },
            new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 64),
            new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            TimeSpan.FromHours(1),
            NetworkName: "noctf-rt-019be6f7882e7cae9389898a98fbfe22-3",
            InternalPorts: [9090],
            Generation: 3,
            RuntimeInstanceId: runtimeId);
    }

    private static V1Service ExistingService(
        ContainerRequest request,
        string name,
        string role,
        string type)
    {
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        labels["noctf.io/resource-role"] = role;
        var selector = new Dictionary<string, string>
        {
            ["noctf.io/runtime-id"] = name,
            ["noctf.io/managed"] = "true",
            ["noctf.io/job-kind"] = "persistent-runtime",
            ["noctf.io/runtime-instance-id"] =
                (request.RuntimeInstanceId ?? request.OperationId).ToString("D"),
            ["noctf.io/generation"] = request.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };
        return new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = "runtime",
                Uid = $"service-{role}-uid",
                Labels = labels
            },
            Spec = new V1ServiceSpec
            {
                Selector = selector,
                Ports = request.ContainerPorts.Select(port => new V1ServicePort
                {
                    Name = $"tcp-{port}",
                    Port = port,
                    TargetPort = port,
                    Protocol = "TCP"
                }).ToList(),
                Type = type
            }
        };
    }

    private static V1Pod ExistingPod(ContainerRequest request, string name)
    {
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        labels["noctf.io/sandbox"] = request.NetworkName!;
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/job-kind"] = request.NetworkPurpose switch
        {
            ContainerNetworkPurpose.AwdChecker => "awd-checker",
            ContainerNetworkPurpose.AwdpVerification => "awdp-verification",
            ContainerNetworkPurpose.PersistentRuntime => "persistent-runtime",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        labels["noctf.io/runtime-instance-id"] = (request.RuntimeInstanceId
            ?? request.OperationId).ToString("D");
        labels["noctf.io/generation"] = request.Generation.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = "runtime",
                Labels = labels
            },
            Status = new V1PodStatus { Phase = "Running" }
        };
    }

    private static V1NetworkPolicy ExistingCallbackPolicy(
        ContainerRequest request,
        int? endPort = null)
    {
        var name = $"noctf-{request.OperationId:N}";
        var runtimeId = request.RuntimeInstanceId ?? request.OperationId;
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        labels["noctf.io/sandbox"] = request.NetworkName!;
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/job-kind"] = "awdp-verification";
        labels["noctf.io/runtime-instance-id"] = runtimeId.ToString("D");
        labels["noctf.io/generation"] = request.Generation.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = $"{name}-callback",
                NamespaceProperty = "runtime",
                Uid = "policy-uid",
                Labels = labels
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>
                    {
                        ["noctf.io/runtime-id"] = name,
                        ["noctf.io/purpose"] = "awdp-checker"
                    }
                },
                PolicyTypes = ["Egress"],
                Egress =
                [
                    new V1NetworkPolicyEgressRule
                    {
                        To =
                        [
                            new V1NetworkPolicyPeer
                            {
                                NamespaceSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        ["kubernetes.io/metadata.name"] = "noctf"
                                    }
                                },
                                PodSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        ["noctf.io/internal-role"] = "awdp-callback"
                                    }
                                }
                            }
                        ],
                        Ports =
                        [
                            new V1NetworkPolicyPort
                            {
                                Protocol = "TCP",
                                Port = 8443,
                                EndPort = endPort
                            }
                        ]
                    },
                    new V1NetworkPolicyEgressRule
                    {
                        To =
                        [
                            new V1NetworkPolicyPeer
                            {
                                NamespaceSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        ["kubernetes.io/metadata.name"] = "kube-system"
                                    }
                                },
                                PodSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        ["k8s-app"] = "kube-dns"
                                    }
                                }
                            }
                        ],
                        Ports =
                        [
                            new V1NetworkPolicyPort
                            {
                                Protocol = "UDP",
                                Port = 53
                            },
                            new V1NetworkPolicyPort
                            {
                                Protocol = "TCP",
                                Port = 53
                            }
                        ]
                    }
                ]
            }
        };
    }

    public enum CallbackPolicyDrift
    {
        EndPort,
        MatchExpression,
        CallbackNamespace
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
        new Dictionary<string, string>(),
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
