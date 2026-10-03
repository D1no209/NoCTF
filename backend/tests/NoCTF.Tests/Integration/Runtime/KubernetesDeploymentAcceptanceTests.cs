using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using k8s;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes.Services;

namespace NoCTF.Tests.Integration.Runtime;

[Category("KubernetesAcceptance"), NotInParallel]
public sealed class KubernetesDeploymentAcceptanceTests
{
    [Test, Timeout(1_800_000)]
    public async Task Real_cluster_capacity_exercises_provider_public_ports_replay_and_cleanup(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_ACCEPTANCE") != "true")
        {
            Skip.Test("Requires explicit isolated cluster, image, public host and report path.");
            return;
        }
        var context = Required("NOCTF_KUBERNETES_CONTEXT");
        if (context == "docker-desktop") throw new InvalidOperationException("Use an isolated acceptance cluster.");
        using var client = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: context));
        var dns = await client.CoreV1.ReadNamespacedServiceAsync("kube-dns", "kube-system", cancellationToken: ct);
        var options = new KubernetesRuntimeOptions(Namespace: "runtime", PublicHost: Required("NOCTF_KUBERNETES_PUBLIC_HOST"),
            PodPidsLimit: 256, ClusterDomain: "cluster.local", ClusterDnsServiceAddress: dns.Spec.ClusterIP,
            ProtectedCidrs: ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"],
            ImagePullSecrets: (Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_IMAGE_PULL_SECRETS") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var lifecycle = new KubernetesContainerLifecycle(client, options);
        var runtime = new KubernetesContainerRuntime(client, lifecycle, options);
        var receipts = new ConcurrentBag<ContainerDeploymentReceipt>();
        var startup = new ConcurrentBag<double>();
        var failures = new ConcurrentBag<string>();
        var count = int.Parse(Required("NOCTF_KUBERNETES_POD_COUNT"), System.Globalization.CultureInfo.InvariantCulture);
        if (count is < 1 or > 1000) throw new InvalidOperationException("Acceptance count must be 1..1000.");
        var image = Required("NOCTF_KUBERNETES_TEST_IMAGE");
        var stage = Stopwatch.StartNew();
        int reachable = 0;
        double cleanupSeconds = 0;
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (_, token) =>
            {
                var request = Request(image);
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var receipt = await runtime.UpAsync(request, token);
                    receipts.Add(receipt);
                    // Receipt replay must preserve the same pod, policy and public port.
                    var replay = await runtime.UpAsync(request, token);
                    if (replay.Services[0].ResourceId != receipt.Services[0].ResourceId
                        || replay.Services[0].PublishedPorts[80] != receipt.Services[0].PublishedPorts[80])
                        throw new InvalidOperationException("Provider replay changed the resource identity.");
                    // Running is a Kubernetes phase, not an application readiness
                    // guarantee. Read-only probes wait for the listener and CNI.
                    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
                    await WaitForHttpAsync(http, $"http://{options.PublicHost}:{receipt.Services[0].PublishedPorts[80]}/", token);
                    startup.Add(Stopwatch.GetElapsedTime(started).TotalSeconds);
                    Interlocked.Increment(ref reachable);
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    // Report only exception types; provider bodies may include protected data.
                    failures.Add(exception.GetType().Name);
                }
            });
        }
        finally
        {
            var cleanupStarted = Stopwatch.GetTimestamp();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await Parallel.ForEachAsync(receipts, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cleanup.Token },
                async (receipt, token) => await runtime.DownAsync(receipt, RuntimeTerminationMode.Force, RuntimeTerminationPolicy.Default, token));
            cleanupSeconds = Stopwatch.GetElapsedTime(cleanupStarted).TotalSeconds;
            var values = startup.Order().ToArray();
            var report = new
            {
                Context = context, TargetPods = count, Started = receipts.Count, Reachable = reachable,
                Failed = failures.Count, FailureTypes = failures.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count()),
                StartupP50Seconds = Percentile(values, .5), StartupP95Seconds = Percentile(values, .95),
                CleanupSeconds = cleanupSeconds, TotalSeconds = stage.Elapsed.TotalSeconds,
                Scope = "Provider lifecycle and direct access; not gameplay or workload sizing evidence",
                ObservedAt = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(Required("NOCTF_KUBERNETES_REPORT"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
        }
        await Assert.That(failures).IsEmpty();
        await Assert.That(reachable).IsEqualTo(count);
        foreach (var receipt in receipts)
        {
            var selector = $"noctf.io/runtime-instance-id={receipt.OperationId:D}";
            await Assert.That((await client.CoreV1.ListNamespacedPodAsync("runtime", labelSelector: selector, cancellationToken: ct)).Items).IsEmpty();
            await Assert.That((await client.CoreV1.ListNamespacedServiceAsync("runtime", labelSelector: selector, cancellationToken: ct)).Items).IsEmpty();
            await Assert.That((await client.NetworkingV1.ListNamespacedNetworkPolicyAsync("runtime", labelSelector: selector, cancellationToken: ct)).Items).IsEmpty();
        }
    }

    private static ContainerRuntimeRequest Request(string image)
    {
        RuntimeServiceDefinition[] services = [new("web", image, CpuCores: .05m, MemoryMiB: 32)];
        return new(Guid.NewGuid(), RuntimeProvider.Kubernetes, services, new Dictionary<string, string>(),
            RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(256))), null, TimeSpan.FromMinutes(2),
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "web")]);
    }
    private static double? Percentile(double[] values, double fraction) => values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * fraction) - 1];
    private static async Task WaitForHttpAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(url, deadline.Token);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException) when (!deadline.IsCancellationRequested) { }
            catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
        }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");
}
