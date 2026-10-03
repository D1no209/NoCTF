using k8s;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Kubernetes;
using NoCTF.Runtime.Kubernetes.Services;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes.Networking;

namespace NoCTF.Runner.Composition;

internal static class KubernetesRuntimeProviderRegistration
{
    internal static IServiceCollection AddKubernetesRuntimeProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isActiveProvider)
    {
        var options = new KubernetesRuntimeOptions(
            configuration["Runtime:Kubernetes:Namespace"] ?? "noctf",
            configuration["Runtime:Kubernetes:PublicHost"] ?? "localhost",
            configuration["Runtime:Kubernetes:CallbackPodLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Kubernetes:CallbackPodLabelValue"] ?? "awdp-callback",
            isActiveProvider
                ? (configuration.GetValue<long?>("Runtime:Execution:ProcessesPerService") ?? 256)
                : 0,
            isActiveProvider
                ? configuration["Runtime:Kubernetes:ClusterDomain"] ?? string.Empty
                : string.Empty,
            isActiveProvider
                ? KubernetesEgressPolicy.ValidateClusterDnsServiceAddress(
                    configuration["Runtime:Kubernetes:ClusterDnsServiceAddress"]
                        ?? string.Empty)
                : string.Empty,
            isActiveProvider
                && configuration.GetValue<bool>("Runtime:Kubernetes:NetworkPolicyRequired"),
            isActiveProvider
                ? KubernetesEgressPolicy.ValidateAndNormalizeProtectedCidrs(
                    configuration.GetSection("Runtime:Kubernetes:ProtectedCidrs")
                        .GetChildren()
                        .Select(section => section.Value ?? string.Empty))
                : null,
            isActiveProvider
                ? configuration["Runtime:Kubernetes:CallbackNamespaceLabelKey"] ?? string.Empty
                : "kubernetes.io/metadata.name",
            isActiveProvider
                ? configuration["Runtime:Kubernetes:CallbackNamespaceLabelValue"] ?? string.Empty
                : "noctf",
            configuration.GetSection("Runtime:Kubernetes:ImagePullSecrets")
                .GetChildren()
                .Select(section => section.Value ?? string.Empty)
                .ToArray());
        foreach (var name in options.ImagePullSecrets ?? [])
        {
            if (name.Length is < 1 or > 253
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    name, @"\A[a-z0-9](?:[-a-z0-9]*[a-z0-9])?(?:\.[a-z0-9](?:[-a-z0-9]*[a-z0-9])?)*\z"))
                throw new InvalidOperationException("Runtime:Kubernetes:ImagePullSecrets must contain Kubernetes Secret names.");
        }
        ValidateActiveProvider(configuration, isActiveProvider);

        services.AddSingleton(options);
        services.AddSingleton<IKubernetes>(_ => new Kubernetes(
            isActiveProvider
                ? KubernetesClientConfiguration.BuildDefaultConfig()
                : new KubernetesClientConfiguration { Host = "http://127.0.0.1" }));
        if (isActiveProvider)
            services.AddHostedService<KubernetesRuntimePoolStartupCheck>();
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<KubernetesContainerRuntime>();
        services.AddSingleton<KubernetesRuntimeResourceReconciler>();
        services.AddSingleton<IRuntimeProviderAvailabilityProbe>(provider =>
            provider.GetRequiredService<KubernetesRuntimeResourceReconciler>());
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<KubernetesRuntimeResourceReconciler>());
        return services;
    }

    private static void ValidateActiveProvider(
        IConfiguration configuration,
        bool isActiveProvider)
    {
        if (!isActiveProvider)
            return;
        if ((configuration.GetValue<long?>("Runtime:Execution:ProcessesPerService") ?? 256) <= 0
            || string.IsNullOrWhiteSpace(configuration["Runtime:Kubernetes:ClusterDomain"])
            || string.IsNullOrWhiteSpace(
                configuration["Runtime:Kubernetes:ClusterDnsServiceAddress"])
            || !configuration.GetValue<bool>("Runtime:Kubernetes:NetworkPolicyRequired")
            || string.IsNullOrWhiteSpace(
                configuration["Runtime:Kubernetes:CallbackNamespaceLabelKey"])
            || string.IsNullOrWhiteSpace(
                configuration["Runtime:Kubernetes:CallbackNamespaceLabelValue"]))
        {
            throw new InvalidOperationException(
                "The active Kubernetes Runtime requires positive PIDs, cluster DNS, network policy, and callback namespace settings.");
        }
    }
}
