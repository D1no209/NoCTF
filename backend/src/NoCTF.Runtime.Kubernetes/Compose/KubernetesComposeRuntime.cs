using k8s;
using k8s.Autorest;
using k8s.Models;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes.Compose;

/// <summary>Owns Kubernetes Compose translation and deployment receipts.</summary>
public sealed class KubernetesComposeRuntime(
    IKubernetes client,
    KubernetesRuntimeOptions options,
    IKomposeConverter converter,
    TimeProvider? clock = null) : IComposeRuntime
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private const string ManagedLabel = "noctf.io/managed";
    private const string RuntimeIdLabel = "noctf.io/runtime-instance-id";
    private const string ExternalReasonExitCode = "ExitCode";

    public async Task<ComposeReceipt> UpAsync(
        ComposeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Kubernetes)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Provider,
                "Kubernetes Compose runtime cannot create another provider.");

        var composeYaml = ComposeRuntimeDefinitionPolicy.PrepareForKubernetes(
            request,
            options.PodPidsLimit);
        var converted = await converter.ConvertAsync(
            composeYaml,
            request.OperationTimeout,
            cancellationToken);
        var manifests = KubernetesComposeManifestPolicy.ParseAndValidate(
            converted,
            request.ServiceResources.Keys.ToHashSet(StringComparer.Ordinal));
        var plan = KubernetesComposeManifestPolicy.ApplyPlatformPolicy(
            manifests,
            request,
            options);

        try
        {
            await EnsureNetworkPolicyAsync(plan.NetworkPolicy, request, cancellationToken);
            foreach (var service in plan.Services)
                await EnsureServiceAsync(service, request, cancellationToken);
            foreach (var deployment in plan.Deployments)
                await EnsureDeploymentAsync(deployment, request, cancellationToken);
            await WaitUntilRunningAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            if (!await TryCleanUpFailedProvisionAsync(plan, request))
                throw new KubernetesComposeCleanupFailedException(
                    "Kubernetes Compose provision failed and its partial resources could not be removed.",
                    exception);
            throw;
        }

        return new(
            request.OperationId,
            RuntimeProvider.Kubernetes,
            request.ProjectName,
            options.Namespace,
            options.PublicHost,
            timeProvider.GetUtcNow());
    }

    public async Task DownAsync(
        ComposeReceipt receipt,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var selector = OwnershipSelector(receipt.OperationId);
        var failures = new List<Exception>();
        string[] deploymentNames = [];
        string[] serviceNames = [];
        string[] policyNames = [];
        try
        {
            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            deploymentNames = deployments.Items
                .Select(deployment => deployment.Metadata.Name)
                .ToArray();
            foreach (var deployment in deployments.Items)
            {
                await TryDeleteAsync(
                    () => client.AppsV1.DeleteNamespacedDeploymentAsync(
                        deployment.Metadata.Name,
                        options.Namespace,
                        body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
                        cancellationToken: cancellationToken),
                    failures);
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            var services = await client.CoreV1.ListNamespacedServiceAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            serviceNames = services.Items
                .Select(service => service.Metadata.Name)
                .ToArray();
            foreach (var service in services.Items)
            {
                await TryDeleteAsync(
                    () => client.CoreV1.DeleteNamespacedServiceAsync(
                        service.Metadata.Name,
                        options.Namespace,
                        body: new V1DeleteOptions(),
                        cancellationToken: cancellationToken),
                    failures);
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            var policies = await client.NetworkingV1.ListNamespacedNetworkPolicyAsync(
                options.Namespace,
                labelSelector: selector,
                cancellationToken: cancellationToken);
            policyNames = policies.Items
                .Select(policy => policy.Metadata.Name)
                .ToArray();
            foreach (var policy in policies.Items)
            {
                await TryDeleteAsync(
                    () => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                        policy.Metadata.Name,
                        options.Namespace,
                        body: new V1DeleteOptions(),
                        cancellationToken: cancellationToken),
                    failures);
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        await WaitUntilDeletedAsync(
            deploymentNames,
            serviceNames,
            policyNames,
            cancellationToken);
    }

    public async Task<ComposeStatus?> GetStatusAsync(
        ComposeReceipt receipt,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var selector = OwnershipSelector(receipt.OperationId);
        var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        if (deployments.Items.Count == 0)
            return new(receipt.ProjectName, RuntimeStatus.Stopped, []);

        var services = await client.CoreV1.ListNamespacedServiceAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        var publicServices = services.Items
            .Where(service => HasLabel(
                service.Metadata.Labels,
                KubernetesComposeManifestPolicy.ResourceRoleLabel,
                "public"))
            .Where(service => service.Metadata.Labels?.ContainsKey(
                KubernetesComposeManifestPolicy.ComposeServiceLabel) == true)
            .ToDictionary(
                service => service.Metadata.Labels[
                    KubernetesComposeManifestPolicy.ComposeServiceLabel],
                StringComparer.Ordinal);
        var runtimeName = $"rt-{receipt.OperationId:N}";
        var serviceStatuses = deployments.Items
            .OrderBy(deployment => deployment.Metadata.Labels[
                KubernetesComposeManifestPolicy.ComposeServiceLabel], StringComparer.Ordinal)
            .Select(deployment =>
            {
                var serviceName = deployment.Metadata.Labels[
                    KubernetesComposeManifestPolicy.ComposeServiceLabel];
                var publishedPorts = publicServices.TryGetValue(serviceName, out var service)
                    ? service.Spec.Ports
                        .Where(port => port.NodePort is > 0)
                        .ToDictionary(port => port.Port, port => port.NodePort!.Value)
                    : new Dictionary<int, int>();
                return new ComposeServiceStatus(
                    serviceName,
                    deployment.Metadata.Name,
                    DeploymentStatus(deployment),
                    publishedPorts,
                    $"{serviceName}.{runtimeName}.{options.Namespace}.svc.{options.ClusterDomain}");
            })
            .ToArray();
        var status = serviceStatuses.All(service => service.Status == RuntimeStatus.Running)
            ? RuntimeStatus.Running
            : serviceStatuses.Any(service => service.Status == RuntimeStatus.Failed)
                ? RuntimeStatus.Failed
                : RuntimeStatus.Starting;
        return new(receipt.ProjectName, status, serviceStatuses);
    }

    public async Task<ContainerExecResult> ExecAsync(
        ComposeReceipt receipt,
        string serviceName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var selector = $"{OwnershipSelector(receipt.OperationId)},"
            + $"{KubernetesComposeManifestPolicy.ComposeServiceLabel}={serviceName}";
        var pods = await client.CoreV1.ListNamespacedPodAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        var pod = pods.Items.SingleOrDefault(item =>
            string.Equals(item.Status?.Phase, "Running", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Compose service '{serviceName}' does not have one running workload.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var demuxer = await client.MuxedStreamNamespacedPodExecAsync(
                pod.Metadata.Name,
                options.Namespace,
                command,
                serviceName,
                false,
                false,
                false,
                false,
                cancellationToken: timeoutSource.Token);
            demuxer.Start();
            using var error = demuxer.GetStream(ChannelIndex.Error, null);
            return new(
                await ReadExitCodeAsync(error, timeoutSource.Token)
                    .WaitAsync(timeoutSource.Token),
                false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(-1, true);
        }
    }

    private async Task EnsureDeploymentAsync(
        V1Deployment desired,
        ComposeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await client.AppsV1.ReadNamespacedDeploymentAsync(
                desired.Metadata.Name,
                options.Namespace,
                cancellationToken: cancellationToken);
            EnsureOwned(existing.Metadata.Labels, request, "Deployment", desired.Metadata.Name);
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.AppsV1.CreateNamespacedDeploymentAsync(
                desired,
                options.Namespace,
                cancellationToken: cancellationToken);
        }
    }

    private async Task EnsureServiceAsync(
        V1Service desired,
        ComposeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await client.CoreV1.ReadNamespacedServiceAsync(
                desired.Metadata.Name,
                options.Namespace,
                cancellationToken: cancellationToken);
            EnsureOwned(existing.Metadata.Labels, request, "Service", desired.Metadata.Name);
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedServiceAsync(
                desired,
                options.Namespace,
                cancellationToken: cancellationToken);
        }
    }

    private async Task EnsureNetworkPolicyAsync(
        V1NetworkPolicy desired,
        ComposeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                desired.Metadata.Name,
                options.Namespace,
                cancellationToken: cancellationToken);
            EnsureOwned(
                existing.Metadata.Labels,
                request,
                "NetworkPolicy",
                desired.Metadata.Name);
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
                desired,
                options.Namespace,
                cancellationToken: cancellationToken);
        }
    }

    private async Task WaitUntilRunningAsync(
        ComposeRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.OperationTimeout);
        var selector = OwnershipSelector(request.OperationId);
        try
        {
            while (true)
            {
                var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
                    options.Namespace,
                    labelSelector: selector,
                    cancellationToken: timeout.Token);
                if (deployments.Items.Count == request.ServiceResources.Count
                    && deployments.Items.All(deployment =>
                        DeploymentStatus(deployment) == RuntimeStatus.Running))
                    return;
                if (deployments.Items.Any(deployment =>
                    DeploymentStatus(deployment) == RuntimeStatus.Failed))
                    throw new InvalidOperationException(
                        "A Kubernetes Compose workload failed to become available.");
                await Task.Delay(250, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Kubernetes Compose workloads did not become available before the operation deadline.");
        }
    }

    private async Task WaitUntilDeletedAsync(
        IReadOnlyList<string> deploymentNames,
        IReadOnlyList<string> serviceNames,
        IReadOnlyList<string> policyNames,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var deploymentExists = false;
            foreach (var name in deploymentNames)
                deploymentExists |= await DeploymentExistsAsync(name, cancellationToken);
            var serviceExists = false;
            foreach (var name in serviceNames)
                serviceExists |= await ServiceExistsAsync(name, cancellationToken);
            var policyExists = false;
            foreach (var name in policyNames)
                policyExists |= await NetworkPolicyExistsAsync(name, cancellationToken);
            if (!deploymentExists && !serviceExists && !policyExists)
                return;
            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task<bool> DeploymentExistsAsync(
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.AppsV1.ReadNamespacedDeploymentAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
            return true;
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> ServiceExistsAsync(
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.CoreV1.ReadNamespacedServiceAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
            return true;
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> NetworkPolicyExistsAsync(
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
            return true;
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> TryCleanUpFailedProvisionAsync(
        KubernetesComposePlan plan,
        ComposeRequest request)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var failures = new List<Exception>();
        foreach (var deployment in plan.Deployments)
        {
            await TryDeleteOwnedAsync(
                () => client.AppsV1.ReadNamespacedDeploymentAsync(
                    deployment.Metadata.Name,
                    options.Namespace,
                    cancellationToken: cleanup.Token),
                labels => EnsureOwned(
                    labels,
                    request,
                    "Deployment",
                    deployment.Metadata.Name),
                () => client.AppsV1.DeleteNamespacedDeploymentAsync(
                    deployment.Metadata.Name,
                    options.Namespace,
                    body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
                    cancellationToken: cleanup.Token),
                failures);
        }
        foreach (var service in plan.Services)
        {
            await TryDeleteOwnedAsync(
                () => client.CoreV1.ReadNamespacedServiceAsync(
                    service.Metadata.Name,
                    options.Namespace,
                    cancellationToken: cleanup.Token),
                labels => EnsureOwned(labels, request, "Service", service.Metadata.Name),
                () => client.CoreV1.DeleteNamespacedServiceAsync(
                    service.Metadata.Name,
                    options.Namespace,
                    body: new V1DeleteOptions(),
                    cancellationToken: cleanup.Token),
                failures);
        }
        await TryDeleteOwnedAsync(
            () => client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                plan.NetworkPolicy.Metadata.Name,
                options.Namespace,
                cancellationToken: cleanup.Token),
            labels => EnsureOwned(
                labels,
                request,
                "NetworkPolicy",
                plan.NetworkPolicy.Metadata.Name),
            () => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                plan.NetworkPolicy.Metadata.Name,
                options.Namespace,
                body: new V1DeleteOptions(),
                cancellationToken: cleanup.Token),
            failures);
        if (failures.Count == 0)
        {
            try
            {
                await WaitUntilDeletedAsync(
                    plan.Deployments.Select(deployment => deployment.Metadata.Name).ToArray(),
                    plan.Services.Select(service => service.Metadata.Name).ToArray(),
                    [plan.NetworkPolicy.Metadata.Name],
                    cleanup.Token);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        return failures.Count == 0;
    }

    private static async Task TryDeleteOwnedAsync<TResource>(
        Func<Task<TResource>> read,
        Action<IDictionary<string, string>?> ensureOwned,
        Func<Task> delete,
        ICollection<Exception> failures)
        where TResource : IKubernetesObject<V1ObjectMeta>
    {
        try
        {
            var resource = await read();
            ensureOwned(resource.Metadata.Labels);
            await delete();
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // The failed create did not leave this deterministic resource behind.
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task TryDeleteAsync(
        Func<Task> delete,
        ICollection<Exception> failures)
    {
        try
        {
            await delete();
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // Deletion is idempotent.
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void EnsureOwned(
        IDictionary<string, string>? labels,
        ComposeRequest request,
        string kind,
        string name)
    {
        if (!HasLabel(labels, ManagedLabel, "true")
            || !HasLabel(labels, RuntimeIdLabel, request.OperationId.ToString("D")))
            throw new InvalidOperationException(
                $"{kind} '{name}' has a different ownership identity.");
    }

    private void ValidateReceipt(ComposeReceipt receipt)
    {
        if (receipt.Provider != RuntimeProvider.Kubernetes
            || !string.Equals(receipt.Namespace, options.Namespace, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Compose receipt does not belong to this Kubernetes Runner Pool.");
    }

    private static string OwnershipSelector(Guid operationId) =>
        $"{ManagedLabel}=true,{RuntimeIdLabel}={operationId:D}";

    private static bool HasLabel(
        IDictionary<string, string>? labels,
        string key,
        string value) =>
        labels is not null
        && labels.TryGetValue(key, out var actual)
        && string.Equals(actual, value, StringComparison.Ordinal);

    private static RuntimeStatus DeploymentStatus(V1Deployment deployment)
    {
        if (deployment.Status?.Conditions?.Any(condition =>
                (string.Equals(condition.Type, "ReplicaFailure", StringComparison.Ordinal)
                    && string.Equals(condition.Status, "True", StringComparison.OrdinalIgnoreCase))
                || (string.Equals(condition.Type, "Progressing", StringComparison.Ordinal)
                    && string.Equals(condition.Status, "False", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        condition.Reason,
                        "ProgressDeadlineExceeded",
                        StringComparison.Ordinal))) == true)
            return RuntimeStatus.Failed;
        return deployment.Status?.AvailableReplicas is > 0
            ? RuntimeStatus.Running
            : RuntimeStatus.Starting;
    }

    private static async Task<int> ReadExitCodeAsync(
        Stream error,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(error);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return 0;
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("status", out var status)
            && string.Equals(status.GetString(), "Success", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (document.RootElement.TryGetProperty("details", out var details)
            && details.TryGetProperty("causes", out var causes))
        {
            var exit = causes.EnumerateArray().FirstOrDefault(cause =>
                cause.TryGetProperty("reason", out var reason)
                && reason.GetString() == ExternalReasonExitCode);
            if (exit.ValueKind != JsonValueKind.Undefined
                && exit.TryGetProperty("message", out var message)
                && int.TryParse(message.GetString(), out var exitCode))
                return exitCode;
        }
        throw new InvalidOperationException(
            "Kubernetes Compose exec ended without an exit code.");
    }
}

public sealed class KubernetesComposeCleanupFailedException(
    string message,
    Exception innerException) : Exception(message, innerException);
