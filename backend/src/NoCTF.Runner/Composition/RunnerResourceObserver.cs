using System.Globalization;
using System.Text.Json;
using Docker.DotNet;
using k8s;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Runner.Composition;

[method: ActivatorUtilitiesConstructor]
public sealed class RunnerResourceObserver(
    IOptions<RunnerOptions> options,
    DockerRuntimeOptions dockerOptions,
    NoCTF.Runtime.Kubernetes.Configuration.KubernetesRuntimeOptions kubernetesOptions,
    IKubernetes kubernetes,
    IClusterLeaseManager leases,
    IHostApplicationLifetime lifetime,
    TimeProvider clock,
    ILogger<RunnerResourceObserver> logger) : IAsyncDisposable
{
    private readonly RunnerPressurePolicy pressure = new(options.Value.Admission);
    private readonly DockerClient docker = new DockerClientBuilder().WithEndpoint(new Uri(dockerOptions.Endpoint)).Build();
    private readonly SemaphoreSlim sampleLock = new(1, 1);
    private (long Total, long Idle)? previousCpu;
    private (long Microseconds, DateTimeOffset At)? previousCgroupCpu;
    private DateTimeOffset? lastAttempt;
    private volatile RunnerAdmissionSnapshot snapshot = new(RunnerAdmissionState.Starting, RunnerAdmissionFailure.ObservationStale, null);
    private IClusterLease? ownership;
    private string? resourceDomain;
    private DateTimeOffset nextOwnershipRenewalAt;
    private int ownershipRenewalFailures;
    private string? lastFailure;

    public RunnerAdmissionSnapshot Current => snapshot;

    public void EnsureFreshAdmission()
    {
        var current = snapshot;
        if (current.State != RunnerAdmissionState.Ready || current.Observation is null
            || clock.GetUtcNow() - current.Observation.ObservedAt > TimeSpan.FromSeconds(options.Value.Admission.FreshnessSeconds))
            throw new TimeoutException("Runner admission is waiting for a fresh healthy resource observation.");
    }

    public async Task<RunnerAdmissionSnapshot> SampleAsync(CancellationToken ct)
    {
        await sampleLock.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (lastAttempt is { } attempted && now - attempted < TimeSpan.FromSeconds(options.Value.Admission.SampleIntervalSeconds))
                return snapshot;
            lastAttempt = now;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(options.Value.Admission.SampleIntervalSeconds));
            var observed = options.Value.Provider == RuntimeProvider.Kubernetes
                ? await ReadKubernetesAsync(budget.Token)
                : await ReadHostAsync(budget.Token);
            if (observed is not null)
                await VerifyOwnershipAsync(observed.ResourceDomain, budget.Token);
            snapshot = pressure.Evaluate(observed, clock.GetUtcNow());
            lastFailure = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            if (snapshot.State != RunnerAdmissionState.Draining)
                snapshot = new(RunnerAdmissionState.Starting, RunnerAdmissionFailure.ObservationStale, null);
            if (lastFailure != exception.GetType().Name)
                logger.LogWarning("Runner observation unavailable after {FailureType}.", exception.GetType().Name);
            lastFailure = exception.GetType().Name;
        }
        finally { sampleLock.Release(); }
        return snapshot;
    }

    private async Task VerifyOwnershipAsync(string domain, CancellationToken ct)
    {
        if (ownership is null)
        {
            ownership = await leases.TryAcquireAsync(
                "resource-domain-" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(domain))).ToLowerInvariant(),
                options.Value.Id,
                ct);
            if (ownership is null)
            {
                snapshot = new(
                    RunnerAdmissionState.Draining,
                    RunnerAdmissionFailure.LedgerRecovering,
                    null);
                lifetime.StopApplication();
                throw new InvalidOperationException("Another Runner owns this provider resource domain.");
            }
            resourceDomain = domain;
            nextOwnershipRenewalAt = clock.GetUtcNow().Add(
                NatsClusterLeaseManager.RenewalInterval);
        }
        else
        {
            if (clock.GetUtcNow() < nextOwnershipRenewalAt)
                return;
            try
            {
                if (resourceDomain != domain) throw new InvalidOperationException("The resource domain changed.");
                await ownership.RenewAsync(ct);
                ownershipRenewalFailures = 0;
                nextOwnershipRenewalAt = clock.GetUtcNow().Add(
                    NatsClusterLeaseManager.RenewalInterval);
            }
            catch
            {
                ownershipRenewalFailures++;
                if (ownershipRenewalFailures >= 2)
                {
                    snapshot = new(
                        RunnerAdmissionState.Draining,
                        RunnerAdmissionFailure.LedgerRecovering,
                        null);
                    lifetime.StopApplication();
                }
                throw;
            }
        }
    }

    private async Task<RunnerResourceObservation?> ReadHostAsync(CancellationToken ct)
    {
        var policy = options.Value.Admission;
        string domain;
        long cpuLimit;
        long memoryLimit;
        if (options.Value.Provider == RuntimeProvider.Docker)
        {
            if (!dockerOptions.Endpoint.StartsWith("unix://", StringComparison.Ordinal))
                return null; // A remote daemon or Windows client cannot use this process's Linux host counters.
            var info = await docker.System.GetSystemInfoAsync(ct);
            if (string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(policy.HostIdentityPath, ct))) return null;
            domain = "docker:" + info.ID;
            cpuLimit = checked(info.NCPU * 1_000_000_000L);
            memoryLimit = info.MemTotal;
        }
        else
        {
            domain = "libvirt:" + (await File.ReadAllTextAsync(policy.HostIdentityPath, ct)).Trim();
            cpuLimit = long.MaxValue;
            memoryLimit = long.MaxValue;
        }
        var memory = (await File.ReadAllLinesAsync(Path.Combine(policy.HostProcRoot, "meminfo"), ct))
            .Select(line => line.Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture));
        var memoryTotal = Math.Min(memoryLimit, checked(memory["MemTotal"] * 1024));
        var memoryAvailable = checked(memory["MemAvailable"] * 1024);
        var stat = await File.ReadAllLinesAsync(Path.Combine(policy.HostProcRoot, "stat"), ct);
        if (cpuLimit == long.MaxValue)
            cpuLimit = checked(stat.Count(line => line.Length > 3 && line.StartsWith("cpu", StringComparison.Ordinal)
                && char.IsAsciiDigit(line[3])) * 1_000_000_000L);
        var counters = stat[0]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var current = (Total: counters.Sum(), Idle: counters[3] + counters[4]);
        var previous = previousCpu;
        previousCpu = current;
        if (previous is null || current.Total <= previous.Value.Total) return null;
        var usage = 1 - (double)(current.Idle - previous.Value.Idle) / (current.Total - previous.Value.Total);
        var load = (await File.ReadAllTextAsync(Path.Combine(policy.HostProcRoot, "loadavg"), ct)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pids = long.Parse(load[3].Split('/')[1], CultureInfo.InvariantCulture);
        var pidsCapacity = long.Parse((await File.ReadAllTextAsync(Path.Combine(policy.HostProcRoot, "sys/kernel/pid_max"), ct)).Trim(), CultureInfo.InvariantCulture);
        var vmstat = await File.ReadAllLinesAsync(Path.Combine(policy.HostProcRoot, "vmstat"), ct);
        var oom = vmstat.FirstOrDefault(line => line.StartsWith("oom_kill ", StringComparison.Ordinal));
        var oomKills = oom is null ? 0 : long.Parse(oom.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
        var cgroup = policy.HostCgroupRoot;
        if (!Directory.Exists(cgroup)) return null;
        var memoryEvents = Path.Combine(cgroup, "memory.events");
        if (File.Exists(memoryEvents))
        {
            var killed = (await File.ReadAllLinesAsync(memoryEvents, ct))
                .FirstOrDefault(line => line.StartsWith("oom_kill ", StringComparison.Ordinal));
            if (killed is not null)
                oomKills = checked(oomKills + long.Parse(killed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture));
        }
        var cgroupMemory = await ReadLimitAsync(Path.Combine(cgroup, "memory.max"), ct);
        if (cgroupMemory is > 0)
        {
            memoryTotal = Math.Min(memoryTotal, cgroupMemory.Value);
            var used = await ReadLimitAsync(Path.Combine(cgroup, "memory.current"), ct);
            if (used is null) return null;
            memoryAvailable = Math.Min(memoryAvailable, Math.Max(0, memoryTotal - used.Value));
        }
        var cgroupPids = await ReadLimitAsync(Path.Combine(cgroup, "pids.max"), ct);
        if (cgroupPids is > 0)
        {
            pidsCapacity = Math.Min(pidsCapacity, cgroupPids.Value);
            pids = await ReadLimitAsync(Path.Combine(cgroup, "pids.current"), ct) ?? pidsCapacity;
        }
        var cpuPath = Path.Combine(cgroup, "cpu.max");
        if (File.Exists(cpuPath))
        {
            var values = (await File.ReadAllTextAsync(cpuPath, ct)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values[0] != "max")
            {
                cpuLimit = Math.Min(cpuLimit, checked((long)(decimal.Parse(values[0], CultureInfo.InvariantCulture)
                    / decimal.Parse(values[1], CultureInfo.InvariantCulture) * 1_000_000_000m)));
                var cpuCounters = await File.ReadAllLinesAsync(Path.Combine(cgroup, "cpu.stat"), ct);
                var usedCpu = long.Parse(cpuCounters.Single(line => line.StartsWith("usage_usec ", StringComparison.Ordinal))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
                var now = clock.GetUtcNow();
                var before = previousCgroupCpu;
                previousCgroupCpu = (usedCpu, now);
                if (before is null || now <= before.Value.At) return null;
                usage = Math.Max(usage, (usedCpu - before.Value.Microseconds) / 1_000_000d
                    / (now - before.Value.At).TotalSeconds / (cpuLimit / 1_000_000_000d));
            }
        }
        return new(domain, clock.GetUtcNow(), memoryTotal, Math.Min(memoryAvailable, memoryTotal), cpuLimit,
            Math.Clamp(usage, 0, 1), pids, pidsCapacity, oomKills);
    }

    private async Task<RunnerResourceObservation?> ReadKubernetesAsync(CancellationToken ct)
    {
        var selector = NoCTF.Runtime.Kubernetes.Configuration.KubernetesRuntimeOptions.PodPidsLimitNodeLabel
            + "=" + kubernetesOptions.PodPidsLimit.ToString(CultureInfo.InvariantCulture);
        var nodes = await kubernetes.CoreV1.ListNodeAsync(labelSelector: selector, cancellationToken: ct);
        var identity = await kubernetes.CoreV1.ReadNamespaceAsync("kube-system", cancellationToken: ct);
        var metrics = JsonSerializer.SerializeToElement(await kubernetes.CustomObjects.ListClusterCustomObjectAsync(
            "metrics.k8s.io", "v1beta1", "nodes", cancellationToken: ct));
        var samples = metrics.GetProperty("items").EnumerateArray().ToDictionary(
            item => item.GetProperty("metadata").GetProperty("name").GetString()!, item => item);
        long memory = 0, usedMemory = 0, cpus = 0, usedCpus = 0;
        var observedAt = clock.GetUtcNow();
        var nodePressure = false;
        var pods = await kubernetes.CoreV1.ListNamespacedPodAsync(kubernetesOptions.Namespace,
            labelSelector: "noctf.io/managed=true", cancellationToken: ct);
        foreach (var status in pods.Items.SelectMany(pod => pod.Status?.ContainerStatuses ?? []))
        foreach (var terminated in new[] { status.State?.Terminated, status.LastState?.Terminated })
            nodePressure |= terminated?.Reason == "OOMKilled" && terminated.FinishedAt is { } finished
                && finished.ToUniversalTime() >= clock.GetUtcNow().AddMinutes(-1).UtcDateTime;
        foreach (var node in nodes.Items.Where(node => node.Spec.Unschedulable != true))
        {
            if (node.Status.Conditions.All(condition => condition.Type != "Ready" || condition.Status != "True")
                || !node.Status.Conditions.Any(condition => condition.Type == "PIDPressure" && condition.Status is "True" or "False")
                || !samples.TryGetValue(node.Metadata.Name, out var sample)) return null;
            memory = checked(memory + (long)node.Status.Allocatable["memory"].ToDecimal());
            cpus = checked(cpus + (long)(node.Status.Allocatable["cpu"].ToDecimal() * 1_000_000_000m));
            usedMemory = checked(usedMemory + (long)new k8s.Models.ResourceQuantity(sample.GetProperty("usage").GetProperty("memory").GetString()).ToDecimal());
            usedCpus = checked(usedCpus + (long)(new k8s.Models.ResourceQuantity(sample.GetProperty("usage").GetProperty("cpu").GetString()).ToDecimal() * 1_000_000_000m));
            var at = sample.GetProperty("timestamp").GetDateTimeOffset();
            if (at < observedAt) observedAt = at;
            nodePressure |= node.Status.Conditions.Any(condition => condition.Status == "True"
                && condition.Type is "MemoryPressure" or "DiskPressure" or "PIDPressure");
        }
        return memory <= 0 || cpus <= 0 ? null : new("kubernetes:" + identity.Metadata.Uid, observedAt,
            memory, Math.Max(0, memory - usedMemory), cpus, (double)usedCpus / cpus,
            null, null, 0, nodePressure, PidPressureConditionAvailable: true);
    }

    private static async Task<long?> ReadLimitAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        var value = (await File.ReadAllTextAsync(path, ct)).Trim();
        return value == "max" ? null : long.Parse(value, CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        if (ownership is not null) await ownership.DisposeAsync();
        docker.Dispose();
        sampleLock.Dispose();
    }
}
