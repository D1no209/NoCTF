using System.Formats.Tar;
using k8s;
using k8s.Autorest;
using k8s.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[NotInParallel]
public sealed class KubernetesOneShotInputArchiveTests
{
    [Test]
    [Timeout(600_000)]
    public async Task RealCluster_PreparesLargeInputBeforeCheckerAndCleansPod(
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip.Test("Integration skipped: NOCTF_KUBERNETES_INTEGRATION is not enabled.");
        }

        var context = Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_CONTEXT")
            ?? throw new InvalidOperationException("An explicit integration context is required.");
        if (context == "docker-desktop") throw new InvalidOperationException("Use an isolated integration cluster.");
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: context));
        var namespaceName = $"noctf-input-it-{Guid.NewGuid():N}";
        await client.CoreV1.CreateNamespaceAsync(new V1Namespace
        {
            Metadata = new V1ObjectMeta
            {
                Name = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    ["noctf.io/purpose"] = "challenge-runtime"
                }
            }
        }, cancellationToken: cancellationToken);
        try
        {
            var kubeDns = await client.CoreV1.ReadNamespacedServiceAsync(
                KubernetesRuntimePoolStartupCheck.KubeDnsServiceName,
                KubernetesRuntimePoolStartupCheck.KubeDnsServiceNamespace,
                cancellationToken: cancellationToken);
            var options = new KubernetesRuntimeOptions(
                Namespace: namespaceName,
                PublicHost: "node.test",
                PodPidsLimit: long.Parse(Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_POD_PIDS_LIMIT") ?? "256", System.Globalization.CultureInfo.InvariantCulture),
                ClusterDomain: "cluster.local",
                ClusterDnsServiceAddress: kubeDns.Spec.ClusterIP
                    ?? throw new InvalidOperationException("kube-dns Service has no ClusterIP."),
                NetworkPolicyRequired: true,
                ProtectedCidrs: ["1.1.1.1/32"]);
            var lifecycle = new KubernetesContainerLifecycle(client, options);
            var operationId = Guid.NewGuid();
            await using var input = CanonicalTar(2 * 1024 * 1024);
            var oneShotInput = new OneShotInputArchive(
                input,
                OneShotInputArchive.RootDestinationPath);

            var result = await lifecycle.RunAsync(
                Request(operationId),
                oneShotInput,
                cancellationToken);

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(input.CanRead).IsTrue();
            await Assert.That(oneShotInput.PreparationCompleted).IsTrue();
            Func<Task> readPod = async () =>
                _ = await client.CoreV1.ReadNamespacedPodAsync(
                    $"noctf-{operationId:N}",
                    namespaceName,
                    cancellationToken: cancellationToken);
            var exception = await Assert.That(readPod).Throws<HttpOperationException>();
            await Assert.That(exception!.Response.StatusCode)
                .IsEqualTo(System.Net.HttpStatusCode.NotFound);

            var invalidOperationId = Guid.NewGuid();
            await using var invalidInput = new MemoryStream("not-a-tar"u8.ToArray());
            Func<Task> invalidAction = async () => _ = await lifecycle.RunAsync(
                Request(invalidOperationId),
                new OneShotInputArchive(
                    invalidInput,
                    OneShotInputArchive.RootDestinationPath),
                cancellationToken);
            await Assert.That(invalidAction).Throws<OneShotInputPreparationException>();
            await AssertPodMissingAsync(
                client,
                namespaceName,
                invalidOperationId,
                cancellationToken);

            var canceledOperationId = Guid.NewGuid();
            await using var canceledInput = CanonicalTar(1024);
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            canceled.CancelAfter(TimeSpan.FromSeconds(2));
            Func<Task> canceledAction = async () => _ = await lifecycle.RunAsync(
                Request(canceledOperationId) with
                {
                    Command = ["/bin/sh", "-c", "sleep 300"]
                },
                new OneShotInputArchive(
                    canceledInput,
                    OneShotInputArchive.RootDestinationPath),
                canceled.Token);
            await Assert.That(canceledAction).Throws<OperationCanceledException>();
            await AssertPodMissingAsync(
                client,
                namespaceName,
                canceledOperationId,
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
                await WaitUntilNamespaceDeletedAsync(client, namespaceName, cleanup.Token);
            }
            catch (HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The test namespace is already absent.
            }
        }
    }

    private static ContainerRequest Request(Guid operationId) => new(operationId, RuntimeProvider.Kubernetes,
        Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_TEST_IMAGE") ?? "busybox:1.36.1", [
            "/bin/sh",
            "-c",
            "test -f /noctf/fix/fix.sh && test \"$(wc -c < /noctf/fix/blob.bin)\" -eq 2097152"
        ], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int>(), new RuntimeResourceLimits(128 * 1024 * 1024, 100, 64), TimeSpan.FromMinutes(2), OperationTimeout: TimeSpan.FromMinutes(2), RuntimeInstanceId: operationId, NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);

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
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static async Task AssertPodMissingAsync(
        IKubernetes client,
        string namespaceName,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        Func<Task> read = async () => _ = await client.CoreV1.ReadNamespacedPodAsync(
            $"noctf-{operationId:N}",
            namespaceName,
            cancellationToken: cancellationToken);
        var exception = await Assert.That(read).Throws<HttpOperationException>();
        await Assert.That(exception!.Response.StatusCode)
            .IsEqualTo(System.Net.HttpStatusCode.NotFound);
    }

    private static MemoryStream CanonicalTar(int payloadBytes)
    {
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "noctf/fix/fix.sh")
            {
                DataStream = new MemoryStream("#!/bin/sh\nexit 0\n"u8.ToArray())
            });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "noctf/fix/blob.bin")
            {
                DataStream = new MemoryStream(new byte[payloadBytes])
            });
        }
        stream.Position = 0;
        return stream;
    }
}
