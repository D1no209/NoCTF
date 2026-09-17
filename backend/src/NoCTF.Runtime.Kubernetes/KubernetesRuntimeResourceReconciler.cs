using System.Net;
using System.Diagnostics;
using k8s;
using k8s.Autorest;
using k8s.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Observability;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes;

public sealed class KubernetesRuntimeResourceReconciler(
    IKubernetes client,
    KubernetesRuntimeOptions options) : IRuntimeManagedResourceReconciler,
    IRuntimeProviderAvailabilityProbe
{
    public RuntimeProvider Provider => RuntimeProvider.Kubernetes;

    public async Task CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        using var response = await client.CoreV1.GetAPIResourcesWithHttpMessagesAsync(
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken)
    {
        var identities = new HashSet<RuntimeResourceIdentity>();
        var selector = ManagedRuntimeSelector();
        var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        AddIdentities(deployments.Items, identities);
        var pods = await client.CoreV1.ListNamespacedPodAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        AddIdentities(pods.Items, identities);
        var services = await client.CoreV1.ListNamespacedServiceAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        AddIdentities(services.Items, identities);
        var policies = await client.NetworkingV1.ListNamespacedNetworkPolicyAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        AddIdentities(policies.Items, identities);
        return identities.ToArray();
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        await DestroyByIdentityAsync(
            identity,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        if (identity.RuntimeInstanceId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(identity));
        var failures = new List<Exception>();
        var networkStarted = Stopwatch.GetTimestamp();
        using (var network = CreateStageToken(
                   cancellationToken,
                   policy.NetworkCleanupTimeout))
        {
            try
            {
                await DeleteNetworkResourcesAsync(identity, network.Token, failures);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        NoCtfTelemetry.RecordRuntimeStopDuration(
            "kubernetes", "managed", "network_cleanup",
            failures.Count == 0 ? "success" : "warning",
            Stopwatch.GetElapsedTime(networkStarted).TotalSeconds);

        var resourcesAbsent = false;
        if (mode == RuntimeTerminationMode.GracefulThenForce)
        {
            var gracefulStarted = Stopwatch.GetTimestamp();
            using var graceful = CreateStageToken(
                cancellationToken,
                policy.GracefulStopTimeout);
            try
            {
                await DeleteComputeResourcesAsync(
                    identity,
                    checked((long)Math.Max(
                        1,
                        Math.Ceiling(policy.GracefulStopTimeout.TotalSeconds))),
                    graceful.Token,
                    failures);
                await WaitUntilDeletedAsync(identity, graceful.Token);
                resourcesAbsent = true;
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "kubernetes", "managed", "graceful", "success",
                    Stopwatch.GetElapsedTime(gracefulStarted).TotalSeconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "kubernetes", "managed", "graceful", "timeout",
                    Stopwatch.GetElapsedTime(gracefulStarted).TotalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "kubernetes", "managed", "graceful", "warning",
                    Stopwatch.GetElapsedTime(gracefulStarted).TotalSeconds);
            }
        }

        if (!resourcesAbsent)
        {
            NoCtfTelemetry.RecordRuntimeStopForce(
                "kubernetes",
                mode == RuntimeTerminationMode.Force ? "requested" : "graceful_failed");
            var forceStarted = Stopwatch.GetTimestamp();
            using var force = CreateStageToken(
                cancellationToken,
                policy.ForceDeleteTimeout);
            try
            {
                await DeleteComputeResourcesAsync(identity, 0, force.Token, failures);
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "kubernetes", "managed", "force_delete",
                    failures.Count == 0 ? "success" : "warning",
                    Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "kubernetes", "managed", "force_delete", "timeout",
                    Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                NoCtfTelemetry.RecordRuntimeStopDuration(
                    "kubernetes", "managed", "force_delete", "warning",
                    Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);
            }
        }

        var verificationStarted = Stopwatch.GetTimestamp();
        using var verification = CreateStageToken(
            cancellationToken,
            policy.VerificationTimeout);
        try
        {
            await WaitUntilDeletedAsync(identity, verification.Token);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "kubernetes", "managed", "verification", "success",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            NoCtfTelemetry.RecordRuntimeStopResourcesRemaining("kubernetes", "managed");
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "kubernetes", "managed", "verification", "timeout",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
            throw new InvalidOperationException(
                "Kubernetes Runtime resources remain after identity-based cleanup.",
                failures.Count == 0 ? null : new AggregateException(failures));
        }
    }

    private async Task DeleteComputeResourcesAsync(
        RuntimeResourceIdentity identity,
        long gracePeriodSeconds,
        CancellationToken cancellationToken,
        ICollection<Exception> failures)
    {
        var selector = IdentitySelector(identity);
        var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        foreach (var deployment in deployments.Items.Where(item =>
                     HasIdentity(item.Metadata.Labels, identity)))
        {
            await TryDeleteAsync(
                () => client.AppsV1.DeleteNamespacedDeploymentAsync(
                    deployment.Metadata.Name,
                    options.Namespace,
                    body: new V1DeleteOptions
                    {
                        GracePeriodSeconds = gracePeriodSeconds,
                        PropagationPolicy = "Foreground"
                    },
                    cancellationToken: cancellationToken),
                failures,
                cancellationToken);
        }

        var pods = await client.CoreV1.ListNamespacedPodAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        foreach (var pod in pods.Items.Where(item =>
                     HasIdentity(item.Metadata.Labels, identity)))
        {
            await TryDeleteAsync(
                () => client.CoreV1.DeleteNamespacedPodAsync(
                    pod.Metadata.Name,
                    options.Namespace,
                    body: new V1DeleteOptions
                    {
                        GracePeriodSeconds = gracePeriodSeconds,
                        PropagationPolicy = "Foreground"
                    },
                    cancellationToken: cancellationToken),
                failures,
                cancellationToken);
        }

    }

    private async Task DeleteNetworkResourcesAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken,
        ICollection<Exception> failures)
    {
        var selector = IdentitySelector(identity);
        var services = await client.CoreV1.ListNamespacedServiceAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        foreach (var service in services.Items.Where(item =>
                     HasIdentity(item.Metadata.Labels, identity)))
        {
            await TryDeleteAsync(
                () => client.CoreV1.DeleteNamespacedServiceAsync(
                    service.Metadata.Name,
                    options.Namespace,
                    body: new V1DeleteOptions(),
                    cancellationToken: cancellationToken),
                failures,
                cancellationToken);
        }

        var policies = await client.NetworkingV1.ListNamespacedNetworkPolicyAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        foreach (var policy in policies.Items.Where(item =>
                     HasIdentity(item.Metadata.Labels, identity)))
        {
            await TryDeleteAsync(
                () => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                    policy.Metadata.Name,
                    options.Namespace,
                    body: new V1DeleteOptions(),
                    cancellationToken: cancellationToken),
                failures,
                cancellationToken);
        }

    }

    private async Task WaitUntilDeletedAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        var selector = IdentitySelector(identity);
        var delays = new[] { 200, 400, 800, 1_000 };
        var attempt = 0;
        while (true)
        {
            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            var pods = await client.CoreV1.ListNamespacedPodAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            var services = await client.CoreV1.ListNamespacedServiceAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            var policies = await client.NetworkingV1.ListNamespacedNetworkPolicyAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            if (deployments.Items.Count == 0
                && pods.Items.Count == 0
                && services.Items.Count == 0
                && policies.Items.Count == 0)
                return;
            await Task.Delay(
                TimeSpan.FromMilliseconds(delays[Math.Min(attempt++, delays.Length - 1)]),
                cancellationToken);
        }
    }

    private static CancellationTokenSource CreateStageToken(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static void AddIdentities<TResource>(
        IEnumerable<TResource> resources,
        ISet<RuntimeResourceIdentity> identities)
        where TResource : IKubernetesObject<V1ObjectMeta>
    {
        foreach (var resource in resources)
        {
            if (TryReadIdentity(resource.Metadata.Labels, out var identity))
                identities.Add(identity);
        }
    }

    private static async Task TryDeleteAsync(
        Func<Task> delete,
        ICollection<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await delete();
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // Exact cleanup is idempotent.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static string ManagedRuntimeSelector() => "noctf.io/managed=true";

    private static string IdentitySelector(RuntimeResourceIdentity identity) =>
        $"{ManagedRuntimeSelector()},"
        + $"noctf.io/runtime-instance-id={identity.RuntimeInstanceId:D}";

    private static bool HasIdentity(
        IDictionary<string, string>? labels,
        RuntimeResourceIdentity identity) =>
        TryReadIdentity(labels, out var actual) && actual == identity;

    private static bool TryReadIdentity(
        IDictionary<string, string>? labels,
        out RuntimeResourceIdentity identity)
    {
        identity = default;
        if (labels is null
            || !labels.TryGetValue("noctf.io/managed", out var managed)
            || !string.Equals(managed, "true", StringComparison.Ordinal)
            || !labels.TryGetValue("noctf.io/runtime-instance-id", out var runtimeText)
            || !Guid.TryParse(runtimeText, out var runtimeId)
            || runtimeId == Guid.Empty)
            return false;
        identity = new(runtimeId);
        return true;
    }
}
