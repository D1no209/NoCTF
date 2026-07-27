using k8s;
using k8s.Autorest;
using k8s.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.Runtime.Kubernetes;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[NotInParallel]
public sealed class KubernetesComposeRuntimeIntegrationTests
{
    [Test]
    [Timeout(600_000)]
    public async Task Real_cluster_enforces_runtime_DNS_network_policy_NodePort_and_cleanup(
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip.Test(
                "Integration skipped: NOCTF_KUBERNETES_INTEGRATION is not enabled.");
        }

        var komposePath = Environment.GetEnvironmentVariable("NOCTF_KOMPOSE_PATH")
            ?? "/usr/local/bin/kompose";
        var ipv6ProbeUrl = Environment.GetEnvironmentVariable(
            "NOCTF_KUBERNETES_IPV6_PROBE_URL");
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        var namespaceName = $"noctf-it-{Guid.NewGuid():N}";
        await client.CoreV1.CreateNamespaceAsync(
            new V1Namespace
            {
                Metadata = new V1ObjectMeta
                {
                    Name = namespaceName,
                    Labels = new Dictionary<string, string>
                    {
                        ["noctf.io/purpose"] = "challenge-runtime"
                    }
                }
            },
            cancellationToken: cancellationToken);
        try
        {
            await VerifyRuntimeAsync(
                client,
                namespaceName,
                komposePath,
                ipv6ProbeUrl,
                cancellationToken);
        }
        finally
        {
            try
            {
                await client.CoreV1.DeleteNamespaceAsync(
                    namespaceName,
                    body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
                    cancellationToken: CancellationToken.None);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                await WaitUntilNamespaceDeletedAsync(
                    client,
                    namespaceName,
                    cleanup.Token);
            }
            catch (HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The test namespace is already absent.
            }
        }
    }

    private static async Task VerifyRuntimeAsync(
        IKubernetes client,
        string namespaceName,
        string komposePath,
        string? ipv6ProbeUrl,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        var kubeDns = await client.CoreV1.ReadNamespacedServiceAsync(
            KubernetesRuntimePoolStartupCheck.KubeDnsServiceName,
            KubernetesRuntimePoolStartupCheck.KubeDnsServiceNamespace,
            cancellationToken: cancellationToken);
        var clusterDnsServiceAddress = kubeDns.Spec.ClusterIP
            ?? throw new InvalidOperationException("kube-dns Service has no ClusterIP.");
        var options = new KubernetesRuntimeOptions(
            Namespace: namespaceName,
            PublicHost: "node.test",
            ImagePullPolicy: "IfNotPresent",
            PodPidsLimit: 512,
            ClusterDomain: "cluster.local",
            ClusterDnsServiceAddress: clusterDnsServiceAddress,
            NetworkPolicyRequired: true,
            ProtectedCidrs: ["1.1.1.1/32"]);
        var runtime = new KubernetesComposeRuntime(
            client,
            options,
            new KomposeConverter(komposePath));
        await EnsureBaselinePolicyEnforcedAsync(
            client,
            runtime,
            options,
            cancellationToken);
        var request = Request(operationId);

        var receipt = await runtime.UpAsync(request, cancellationToken);
        var status = await runtime.GetStatusAsync(receipt, cancellationToken);

        await Assert.That(status).IsNotNull();
        await Assert.That(status!.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(status.Services).Count().IsEqualTo(2);
        var web = status.Services.Single(service => service.Name == "web");
        await Assert.That(web.PublishedPorts.ContainsKey(8080)).IsTrue();
        await Assert.That(web.InternalHost).IsEqualTo(
            $"web.rt-{operationId:N}.{namespaceName}.svc.cluster.local");

        var shortDns = await runtime.ExecAsync(
            receipt,
            "web",
            [
                "/bin/sh",
                "-c",
                "for i in $(seq 1 30); do "
                + "wget -T 3 -q -O- http://db:9090 | grep -q db && exit 0; "
                + "sleep 1; done; exit 1"
            ],
            TimeSpan.FromSeconds(40),
            cancellationToken);
        await Assert.That(shortDns.ExitCode).IsEqualTo(0);
        var sameRuntime = await runtime.ExecAsync(
            receipt,
            "web",
            [
                "/bin/sh",
                "-c",
                $"wget -T 5 -q -O- http://db.rt-{operationId:N}."
                + $"{namespaceName}.svc.cluster.local:9090 | grep -q db"
            ],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(sameRuntime.ExitCode).IsEqualTo(0);
        var deniedInternet = await runtime.ExecAsync(
            receipt,
            "web",
            [
                "/bin/sh",
                "-c",
                "timeout 5 wget -q -O /dev/null http://example.com"
            ],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(deniedInternet.ExitCode).IsNotEqualTo(0);

        var internetOperationId = Guid.NewGuid();
        var internetReceipt = await runtime.UpAsync(
            Request(internetOperationId) with
            {
                EgressPolicy = RuntimeEgressPolicy.InternetOnly
            },
            cancellationToken);
        var allowedInternet = await runtime.ExecAsync(
            internetReceipt,
            "web",
            [
                "/bin/sh",
                "-c",
                "wget -T 10 -q -O /dev/null http://example.com"
            ],
            TimeSpan.FromSeconds(20),
            cancellationToken);
        await Assert.That(allowedInternet.ExitCode).IsEqualTo(0);
        var protectedInternet = await runtime.ExecAsync(
            internetReceipt,
            "web",
            [
                "/bin/sh",
                "-c",
                "timeout 5 wget -q -O /dev/null http://1.1.1.1"
            ],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(protectedInternet.ExitCode).IsNotEqualTo(0);
        if (!string.IsNullOrWhiteSpace(ipv6ProbeUrl))
        {
            await VerifyIpv6EgressPolicyAsync(
                client,
                runtime,
                namespaceName,
                internetReceipt,
                ipv6ProbeUrl,
                cancellationToken);
        }

        var runtimeLabels = OwnershipLabels(operationId, 1);
        var runtimeName = $"rt-{operationId:N}";
        await client.CoreV1.CreateNamespacedPodAsync(
            ProbePod(
                $"notready-{operationId:N}"[..41],
                "notready",
                runtimeName,
                runtimeLabels,
                neverReady: true),
            namespaceName,
            cancellationToken: cancellationToken);
        await WaitUntilPodRunningAsync(
            client,
            namespaceName,
            $"notready-{operationId:N}"[..41],
            cancellationToken);
        var notReadyDns = await runtime.ExecAsync(
            receipt,
            "web",
            [
                "/bin/sh",
                "-c",
                $"for i in $(seq 1 30); do nslookup "
                + $"notready.{runtimeName}.{namespaceName}.svc.cluster.local "
                + ">/dev/null 2>&1 && exit 0; sleep 1; done; exit 1"
            ],
            TimeSpan.FromSeconds(40),
            cancellationToken);
        await Assert.That(notReadyDns.ExitCode).IsEqualTo(0);

        var attackerId = Guid.NewGuid();
        var attackerName = $"attacker-{attackerId:N}"[..40];
        var attackerLabels = OwnershipLabels(attackerId, 1);
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
            new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"{attackerName}-egress",
                    NamespaceProperty = namespaceName
                },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector
                    {
                        MatchLabels = attackerLabels
                    },
                    PolicyTypes = ["Egress"],
                    Egress = [new V1NetworkPolicyEgressRule()]
                }
            },
            namespaceName,
            cancellationToken: cancellationToken);
        await client.CoreV1.CreateNamespacedPodAsync(
            ProbePod(
                attackerName,
                "attacker",
                null,
                attackerLabels,
                neverReady: false),
            namespaceName,
            cancellationToken: cancellationToken);
        await WaitUntilPodRunningAsync(
            client,
            namespaceName,
            attackerName,
            cancellationToken);
        var attackerReceipt = new ComposeReceipt(
            attackerId,
            RuntimeProvider.Kubernetes,
            "attacker",
            namespaceName,
            "node.test",
            1,
            DateTimeOffset.UtcNow);
        var crossRuntime = await runtime.ExecAsync(
            attackerReceipt,
            "attacker",
            [
                "/bin/sh",
                "-c",
                $"sleep 2; timeout 5 wget -q -O- "
                + $"http://db.{runtimeName}.{namespaceName}.svc.cluster.local:9090"
            ],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(crossRuntime.ExitCode).IsNotEqualTo(0);

        var nodes = await client.CoreV1.ListNodeAsync(
            cancellationToken: cancellationToken);
        var nodeAddress = nodes.Items
            .SelectMany(node => node.Status.Addresses)
            .First(address => address.Type == "InternalIP")
            .Address;
        var nodePort = web.PublishedPorts[8080];
        var publicClientId = Guid.NewGuid();
        var publicClientName = $"public-{publicClientId:N}"[..38];
        var publicClientLabels = OwnershipLabels(publicClientId, 1, "public");
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
            new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"{publicClientName}-egress",
                    NamespaceProperty = namespaceName
                },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector
                    {
                        MatchLabels = publicClientLabels
                    },
                    PolicyTypes = ["Egress"],
                    Egress = [new V1NetworkPolicyEgressRule()]
                }
            },
            namespaceName,
            cancellationToken: cancellationToken);
        await client.CoreV1.CreateNamespacedPodAsync(
            ProbePod(
                publicClientName,
                "public",
                null,
                publicClientLabels,
                neverReady: false),
            namespaceName,
            cancellationToken: cancellationToken);
        await WaitUntilPodRunningAsync(
            client,
            namespaceName,
            publicClientName,
            cancellationToken);
        var publicClientReceipt = new ComposeReceipt(
            publicClientId,
            RuntimeProvider.Kubernetes,
            "public",
            namespaceName,
            "node.test",
            1,
            DateTimeOffset.UtcNow);
        var publicIngress = await runtime.ExecAsync(
            publicClientReceipt,
            "public",
            [
                "/bin/sh",
                "-c",
                $"wget -T 5 -q -O- http://{nodeAddress}:{nodePort} | grep -q web"
            ],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(publicIngress.ExitCode).IsEqualTo(0);

        var reconciler = new KubernetesRuntimeResourceReconciler(client, options);
        var containerOperationId = Guid.NewGuid();
        var containerLifecycle = new KubernetesContainerLifecycle(client, options);
        var containerRequest = new ContainerRequest(
            containerOperationId,
            RuntimeProvider.Kubernetes,
            "busybox:1.36.1",
            [
                "/bin/sh",
                "-c",
                "mkdir -p /www && echo container > /www/index.html "
                + "&& exec httpd -f -p 8080 -h /www"
            ],
            new Dictionary<string, string>(),
            OwnershipLabels(containerOperationId, 1, "container"),
            new Dictionary<int, int> { [8080] = 0 },
            new RuntimeResourceLimits(67_108_864, 100_000_000, 512),
            new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
            TimeSpan.FromMinutes(5),
            OperationTimeout: TimeSpan.FromMinutes(2),
            NetworkIsolation: ContainerNetworkIsolation.Isolated,
            Generation: 1,
            RuntimeInstanceId: containerOperationId);
        _ = await IsolatedContainerProvisioner.ProvisionAsync(
            containerLifecycle,
            containerLifecycle,
            containerRequest,
            DateTimeOffset.UtcNow,
            cancellationToken);
        var managed = await reconciler.ListManagedAsync(cancellationToken);
        await Assert.That(managed)
            .Contains(new RuntimeResourceIdentity(operationId, 1));
        await Assert.That(managed)
            .Contains(new RuntimeResourceIdentity(internetOperationId, 1));
        await Assert.That(managed)
            .Contains(new RuntimeResourceIdentity(containerOperationId, 1));
        await reconciler.DestroyByIdentityAsync(
            new(operationId, 1),
            cancellationToken);
        await reconciler.DestroyByIdentityAsync(
            new(internetOperationId, 1),
            cancellationToken);
        await reconciler.DestroyByIdentityAsync(
            new(containerOperationId, 1),
            cancellationToken);
        await AssertRuntimeResourcesDeletedAsync(
            client,
            namespaceName,
            operationId,
            cancellationToken);
        await AssertRuntimeResourcesDeletedAsync(
            client,
            namespaceName,
            internetOperationId,
            cancellationToken);
        await AssertRuntimeResourcesDeletedAsync(
            client,
            namespaceName,
            containerOperationId,
            cancellationToken);
    }

    private static async Task AssertRuntimeResourcesDeletedAsync(
        IKubernetes client,
        string namespaceName,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var selector = $"noctf.io/managed=true,"
            + $"noctf.io/runtime-instance-id={operationId:D},"
            + "noctf.io/generation=1";
        var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
            namespaceName,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        var pods = await client.CoreV1.ListNamespacedPodAsync(
            namespaceName,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        var services = await client.CoreV1.ListNamespacedServiceAsync(
            namespaceName,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        var policies = await client.NetworkingV1.ListNamespacedNetworkPolicyAsync(
            namespaceName,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        await Assert.That(deployments.Items).IsEmpty();
        await Assert.That(pods.Items).IsEmpty();
        await Assert.That(services.Items).IsEmpty();
        await Assert.That(policies.Items).IsEmpty();
    }

    private static async Task VerifyIpv6EgressPolicyAsync(
        IKubernetes client,
        KubernetesComposeRuntime runtime,
        string namespaceName,
        ComposeReceipt internetReceipt,
        string probeUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(probeUrl, UriKind.Absolute, out var uri)
            || !System.Net.IPAddress.TryParse(uri.Host, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            throw new InvalidOperationException(
                "NOCTF_KUBERNETES_IPV6_PROBE_URL must use an IPv6 address host.");
        }

        var controlId = Guid.NewGuid();
        var controlName = $"ipv6-control-{controlId:N}"[..45];
        var controlLabels = OwnershipLabels(controlId, 1, "ipv6-control");
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
            new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"{controlName}-egress",
                    NamespaceProperty = namespaceName
                },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector
                    {
                        MatchLabels = controlLabels
                    },
                    PolicyTypes = ["Egress"],
                    Egress = [new V1NetworkPolicyEgressRule()]
                }
            },
            namespaceName,
            cancellationToken: cancellationToken);
        await client.CoreV1.CreateNamespacedPodAsync(
            ProbePod(
                controlName,
                "ipv6-control",
                null,
                controlLabels,
                neverReady: false),
            namespaceName,
            cancellationToken: cancellationToken);
        await WaitUntilPodRunningAsync(
            client,
            namespaceName,
            controlName,
            cancellationToken);
        var controlReceipt = new ComposeReceipt(
            controlId,
            RuntimeProvider.Kubernetes,
            "ipv6-control",
            namespaceName,
            "node.test",
            1,
            DateTimeOffset.UtcNow);
        var allowedControl = await runtime.ExecAsync(
            controlReceipt,
            "ipv6-control",
            ["wget", "-T", "10", "-q", "-O", "/dev/null", probeUrl],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        await Assert.That(allowedControl.ExitCode).IsEqualTo(0);

        var deniedRuntime = await runtime.ExecAsync(
            internetReceipt,
            "web",
            ["wget", "-T", "5", "-q", "-O", "/dev/null", probeUrl],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(deniedRuntime.ExitCode).IsNotEqualTo(0);
    }

    private static async Task EnsureBaselinePolicyEnforcedAsync(
        IKubernetes client,
        KubernetesComposeRuntime runtime,
        KubernetesRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
            new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta
                {
                    Name = KubernetesRuntimePoolStartupCheck.PolicyName,
                    NamespaceProperty = options.Namespace,
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
            },
            options.Namespace,
            cancellationToken: cancellationToken);
        var startupCheck = new KubernetesRuntimePoolStartupCheck(client, options);
        await startupCheck.StartAsync(cancellationToken);

        var probeId = Guid.NewGuid();
        var probeName = $"baseline-{probeId:N}"[..40];
        await client.CoreV1.CreateNamespacedPodAsync(
            ProbePod(
                probeName,
                "baseline",
                null,
                OwnershipLabels(probeId, 1, "baseline"),
                neverReady: false),
            options.Namespace,
            cancellationToken: cancellationToken);
        await WaitUntilPodRunningAsync(
            client,
            options.Namespace,
            probeName,
            cancellationToken);
        var receipt = new ComposeReceipt(
            probeId,
            RuntimeProvider.Kubernetes,
            "baseline",
            options.Namespace,
            options.PublicHost,
            1,
            DateTimeOffset.UtcNow);
        using var convergence = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        convergence.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var result = await runtime.ExecAsync(
                receipt,
                "baseline",
                ["/bin/sh", "-c", "timeout 5 wget -q -O /dev/null http://1.1.1.1"],
                TimeSpan.FromSeconds(5),
                convergence.Token);
            if (result.ExitCode != 0)
                break;
            await Task.Delay(250, convergence.Token);
        }
        await client.CoreV1.DeleteNamespacedPodAsync(
            probeName,
            options.Namespace,
            body: new V1DeleteOptions(),
            cancellationToken: cancellationToken);
    }

    private static V1Pod ProbePod(
        string name,
        string containerName,
        string? subdomain,
        IDictionary<string, string> labels,
        bool neverReady) =>
        new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = labels
            },
            Spec = new V1PodSpec
            {
                AutomountServiceAccountToken = false,
                Hostname = containerName,
                Subdomain = subdomain,
                RestartPolicy = "Never",
                TerminationGracePeriodSeconds = 0,
                Containers =
                [
                    new V1Container
                    {
                        Name = containerName,
                        Image = "busybox:1.36.1",
                        Command = ["/bin/sh", "-c", "sleep 300"],
                        ReadinessProbe = neverReady
                            ? new V1Probe
                            {
                                Exec = new V1ExecAction
                                {
                                    Command = ["/bin/false"]
                                },
                                PeriodSeconds = 1
                            }
                            : null
                    }
                ]
            }
        };

    private static async Task WaitUntilPodRunningAsync(
        IKubernetes client,
        string namespaceName,
        string podName,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(
                podName,
                namespaceName,
                cancellationToken: cancellationToken);
            if (pod.Status?.Phase == "Running")
                return;
            if (pod.Status?.Phase == "Failed")
                throw new InvalidOperationException($"Probe Pod '{podName}' failed.");
            await Task.Delay(250, cancellationToken);
        }
    }

    private static async Task WaitUntilNamespaceDeletedAsync(
        IKubernetes client,
        string namespaceName,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                _ = await client.CoreV1.ReadNamespaceAsync(
                    namespaceName,
                    cancellationToken: cancellationToken);
            }
            catch (HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return;
            }
            await Task.Delay(250, cancellationToken);
        }
    }

    private static Dictionary<string, string> OwnershipLabels(
        Guid operationId,
        int generation,
        string serviceName = "attacker") =>
        new(StringComparer.Ordinal)
        {
            ["noctf.io/managed"] = "true",
            ["noctf.io/job-kind"] = "persistent-runtime",
            ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
            ["noctf.io/generation"] = generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [KubernetesComposeManifestPolicy.ComposeServiceLabel] = serviceName
        };

    private static ComposeRequest Request(Guid operationId) => new(
        operationId,
        RuntimeProvider.Kubernetes,
        1,
        $"it-{operationId:N}",
        """
        services:
          web:
            image: busybox:1.36.1
            command:
              - /bin/sh
              - -c
              - mkdir -p /www && echo web > /www/index.html && exec httpd -f -p 8080 -h /www
            expose:
              - "8080"
          db:
            image: busybox:1.36.1
            command:
              - /bin/sh
              - -c
              - mkdir -p /www && echo db > /www/index.html && exec httpd -f -p 9090 -h /www
            expose:
              - "9090"
        """,
        new Dictionary<string, string>(),
        new Dictionary<string, string>
        {
            ["noctf.io/job-kind"] = "persistent-runtime"
        },
        new Dictionary<string, RuntimeResourceLimits>
        {
            ["web"] = new(67_108_864, 100_000_000, 0),
            ["db"] = new(67_108_864, 100_000_000, 0)
        },
        new(134_217_728, 200_000_000, 0),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(3),
        [
            new RuntimeUrlBinding(
                "http://{HOST}:{PORT}",
                RuntimeExposure.Participants,
                ContainerPort: 8080,
                ServiceName: "web")
        ]);
}
