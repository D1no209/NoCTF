using System.Net;
using k8s;
using k8s.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;

namespace NoCTF.Runtime.Kubernetes.Services;

/// <summary>Creates native Pods and optional headless discovery without Compose translation.</summary>
public sealed class KubernetesContainerRuntime(IKubernetes client, KubernetesContainerLifecycle containers,
    KubernetesRuntimeOptions options, TimeProvider? clock = null) : NamedContainerRuntime(containers, containers, clock)
{
    protected override RuntimeProvider Provider => RuntimeProvider.Kubernetes;
    protected override string PublicHost => options.PublicHost;
    protected override string Namespace => options.Namespace;

    protected override async Task<RuntimeNetworkAttachment> PrepareNetworkAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken)
    {
        var ports = (request.UrlBindings ?? []).Select(binding => binding.ContainerPort!.Value).Distinct().ToArray();
        var privatePorts = ports.Concat(request.ControlCheckUrlBinding is { ContainerPort: int controlPort } ? [controlPort] : []).Distinct().ToArray();
        var policy = await containers.CreateIsolatedNetworkAsync(new(new(request.OperationId), request.Purpose,
            request.EgressPolicy, request.AccessMode == RuntimeAccessMode.WsrxOnly ? [] : ports,
            request.Purpose == ContainerNetworkPurpose.AwdpVerification ? request.Services.Single().InternalPorts!.Single() : null,
            request.AccessMode == RuntimeAccessMode.Direct && request.ControlCheckUrlBinding is null ? [] : privatePorts), cancellationToken);
        if (request.Services.Count == 1) return new(policy);
        var labels = new Dictionary<string, string>
        {
            ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = request.OperationId.ToString("D"),
            ["noctf.io/resource-role"] = "discovery"
        };
        var desired = new V1Service
        {
            Metadata = new() { Name = request.ProjectName, NamespaceProperty = options.Namespace, Labels = labels },
            Spec = new()
            {
                ClusterIP = "None", PublishNotReadyAddresses = true,
                Selector = new Dictionary<string, string> { ["noctf.io/runtime-instance-id"] = request.OperationId.ToString("D"), ["noctf.io/job-kind"] = "persistent-runtime" }
            }
        };
        try { await client.CoreV1.CreateNamespacedServiceAsync(desired, options.Namespace, cancellationToken: cancellationToken); }
        catch (k8s.Autorest.HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await client.CoreV1.ReadNamespacedServiceAsync(request.ProjectName, options.Namespace, cancellationToken: cancellationToken);
            if (Label(existing.Metadata.Labels, "noctf.io/runtime-instance-id") != request.OperationId.ToString("D")
                || existing.Spec.ClusterIP != "None" || Label(existing.Spec.Selector, "noctf.io/runtime-instance-id") != request.OperationId.ToString("D"))
                throw new InvalidOperationException("Discovery Service has a different owner or contract.");
        }
        return new(policy, DiscoveryServiceName: request.ProjectName);
    }

    protected override async Task RemoveNetworkAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken)
    {
        if ((receipt.DiscoveryServiceName ?? (receipt.Services.Count > 1 ? receipt.ProjectName : null)) is { } name)
        {
            try
            {
                var service = await client.CoreV1.ReadNamespacedServiceAsync(name, options.Namespace, cancellationToken: cancellationToken);
                if (Label(service.Metadata.Labels, "noctf.io/runtime-instance-id") != receipt.OperationId.ToString("D"))
                    throw new InvalidOperationException("Discovery Service belongs to another Runtime.");
                await client.CoreV1.DeleteNamespacedServiceAsync(name, options.Namespace,
                    body: new() { Preconditions = new() { Uid = service.Metadata.Uid } }, cancellationToken: cancellationToken);
                while (true)
                {
                    _ = await client.CoreV1.ReadNamespacedServiceAsync(name, options.Namespace, cancellationToken: cancellationToken);
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                }
            }
            catch (k8s.Autorest.HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound) { }
        }
        foreach (var policyName in new[] { receipt.ProjectName, $"noctf-awdp-{receipt.OperationId:N}" })
        {
            try
            {
                var policy = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(policyName, options.Namespace, cancellationToken: cancellationToken);
                if (Label(policy.Metadata.Labels, "noctf.io/runtime-instance-id") != receipt.OperationId.ToString("D"))
                    throw new InvalidOperationException("Runtime policy belongs to another owner.");
                await client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(policyName, options.Namespace,
                    body: new() { Preconditions = new() { Uid = policy.Metadata.Uid } }, cancellationToken: cancellationToken);
                while (await containers.IsolatedNetworkExistsAsync(policyName, cancellationToken))
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
            catch (k8s.Autorest.HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound) { }
        }
    }
    private static string? Label(IDictionary<string, string>? values, string key) => values is not null && values.TryGetValue(key, out var value) ? value : null;
}
