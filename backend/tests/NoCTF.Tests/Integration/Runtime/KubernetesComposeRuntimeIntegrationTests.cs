using k8s;
using k8s.Autorest;
using k8s.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Configuration;

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
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
        var namespaceName = $"noctf-it-{Guid.NewGuid():N}";
        await client.CoreV1.CreateNamespaceAsync(
            new V1Namespace
            {
                Metadata = new V1ObjectMeta { Name = namespaceName }
            },
            cancellationToken: cancellationToken);
        try
        {
            await VerifyRuntimeAsync(
                client,
                namespaceName,
                komposePath,
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
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        var options = new KubernetesRuntimeOptions(
            Namespace: namespaceName,
            PublicHost: "node.test",
            ImagePullPolicy: "IfNotPresent",
            PodPidsLimit: 512,
            ClusterDomain: "cluster.local",
            NetworkPolicyRequired: true);
        var runtime = new KubernetesComposeRuntime(
            client,
            options,
            new KomposeConverter(komposePath));
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
        await client.CoreV1.CreateNamespacedPodAsync(
            ProbePod(
                attackerName,
                "attacker",
                null,
                OwnershipLabels(attackerId, 1),
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
                $"sleep 2; wget -T 3 -q -O- "
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
        var publicIngress = await runtime.ExecAsync(
            attackerReceipt,
            "attacker",
            [
                "/bin/sh",
                "-c",
                $"wget -T 5 -q -O- http://{nodeAddress}:{nodePort} | grep -q web"
            ],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        await Assert.That(publicIngress.ExitCode).IsEqualTo(0);

        await runtime.DownAsync(receipt, cancellationToken);
        var selector = $"noctf.io/managed=true,"
            + $"noctf.io/runtime-instance-id={operationId:D},"
            + "noctf.io/generation=1";
        var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
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
        await Assert.That(services.Items).IsEmpty();
        await Assert.That(policies.Items).IsEmpty();
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
        int generation) =>
        new(StringComparer.Ordinal)
        {
            ["noctf.io/managed"] = "true",
            ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
            ["noctf.io/generation"] = generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [KubernetesComposeManifestPolicy.ComposeServiceLabel] = "attacker"
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
        new Dictionary<string, string>(),
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
