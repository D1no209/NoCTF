using System.Globalization;
using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using NoCTF.Application.Runtime.Provisioning;
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
        if (identity.RuntimeInstanceId == Guid.Empty || identity.Generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(identity));
        var selector = IdentitySelector(identity);
        var failures = new List<Exception>();
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
                    body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
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
                    body: new V1DeleteOptions { GracePeriodSeconds = 0 },
                    cancellationToken: cancellationToken),
                failures,
                cancellationToken);
        }

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

        if (failures.Count > 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        await WaitUntilDeletedAsync(identity, cancellationToken);
    }

    private async Task WaitUntilDeletedAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        var selector = IdentitySelector(identity);
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
            await Task.Delay(250, cancellationToken);
        }
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
        + $"noctf.io/runtime-instance-id={identity.RuntimeInstanceId:D},"
        + $"noctf.io/generation={identity.Generation.ToString(CultureInfo.InvariantCulture)}";

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
            || runtimeId == Guid.Empty
            || !labels.TryGetValue("noctf.io/generation", out var generationText)
            || !int.TryParse(
                generationText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var generation)
            || generation <= 0)
            return false;
        identity = new(runtimeId, generation);
        return true;
    }
}
