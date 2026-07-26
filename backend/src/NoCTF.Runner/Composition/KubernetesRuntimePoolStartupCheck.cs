using k8s;
using k8s.Autorest;
using k8s.Models;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runner.Composition;

public sealed class KubernetesRuntimePoolStartupCheck(
    IKubernetes client,
    KubernetesRuntimeOptions options) : IHostedService
{
    public const string PolicyName = "noctf-runtime-baseline-deny";
    public const string PurposeLabel = "runtime-baseline";
    public const string CiliumConfigNamespace = "kube-system";
    public const string CiliumConfigMapName = "cilium-config";
    public const string CiliumPolicyEnforcementKey = "enable-policy";
    public const string KubeDnsServiceNamespace = "kube-system";
    public const string KubeDnsServiceName = "kube-dns";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        V1ConfigMap ciliumConfig;
        try
        {
            ciliumConfig = await client.CoreV1.ReadNamespacedConfigMapAsync(
                CiliumConfigMapName,
                CiliumConfigNamespace,
                cancellationToken: cancellationToken);
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Kubernetes Runner Pool requires Cilium ConfigMap "
                + $"'{CiliumConfigNamespace}/{CiliumConfigMapName}'.",
                exception);
        }
        if (ciliumConfig.Data?.TryGetValue(
                CiliumPolicyEnforcementKey,
                out var enforcementMode) != true
            || !string.Equals(enforcementMode, "always", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cilium '{CiliumPolicyEnforcementKey}' must be configured as 'always'.");
        }

        V1Service kubeDns;
        try
        {
            kubeDns = await client.CoreV1.ReadNamespacedServiceAsync(
                KubeDnsServiceName,
                KubeDnsServiceNamespace,
                cancellationToken: cancellationToken);
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Kubernetes Runner Pool requires DNS Service "
                + $"'{KubeDnsServiceNamespace}/{KubeDnsServiceName}'.",
                exception);
        }
        var dnsServiceAddresses = new HashSet<string>(
            kubeDns.Spec?.ClusterIPs ?? [],
            StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(kubeDns.Spec?.ClusterIP))
            dnsServiceAddresses.Add(kubeDns.Spec.ClusterIP);
        if (!dnsServiceAddresses.Contains(options.ClusterDnsServiceAddress))
        {
            throw new InvalidOperationException(
                $"Runtime:Kubernetes:ClusterDnsServiceAddress "
                + $"'{options.ClusterDnsServiceAddress}' does not match Service "
                + $"'{KubeDnsServiceNamespace}/{KubeDnsServiceName}'.");
        }

        V1NetworkPolicy policy;
        try
        {
            policy = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                PolicyName,
                options.Namespace,
                cancellationToken: cancellationToken);
        }
        catch (HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Kubernetes Runner Pool requires deployment-owned NetworkPolicy "
                + $"'{options.Namespace}/{PolicyName}'.",
                exception);
        }

        var selector = policy.Spec?.PodSelector;
        var selectsAllPods = selector is not null
            && selector.MatchLabels?.Count is not > 0
            && selector.MatchExpressions?.Count is not > 0;
        var policyTypes = policy.Spec?.PolicyTypes ?? [];
        var deniesIngress = policyTypes.Contains("Ingress", StringComparer.Ordinal)
            && policy.Spec?.Ingress?.Count is not > 0;
        var deniesEgress = policyTypes.Contains("Egress", StringComparer.Ordinal)
            && policy.Spec?.Egress?.Count is not > 0;
        var deploymentOwned =
            policy.Metadata?.Labels?.TryGetValue("noctf.io/purpose", out var purpose) == true
            && string.Equals(purpose, PurposeLabel, StringComparison.Ordinal);
        if (!selectsAllPods || !deniesIngress || !deniesEgress || !deploymentOwned)
        {
            throw new InvalidOperationException(
                $"NetworkPolicy '{options.Namespace}/{PolicyName}' must be deployment-owned, "
                + "select every Pod, and deny all ingress and egress by default.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
