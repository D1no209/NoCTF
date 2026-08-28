using k8s;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Kubernetes;
using NoCTF.Runtime.Kubernetes.Compose;
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
            configuration["Runtime:Kubernetes:ImagePullPolicy"] ?? "IfNotPresent",
            configuration["Runtime:Kubernetes:CallbackPodLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Kubernetes:CallbackPodLabelValue"] ?? "awdp-callback",
            isActiveProvider
                ? configuration.GetValue<long>("Runtime:Kubernetes:PodPidsLimit")
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
                : "noctf");
        ValidateActiveProvider(configuration, isActiveProvider);

        services.AddSingleton(options);
        services.AddSingleton<IKubernetes>(_ =>
            new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()));
        if (isActiveProvider)
            services.AddHostedService<KubernetesRuntimePoolStartupCheck>();
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<IKomposeConverter>(new KomposeConverter());
        services.AddSingleton<KubernetesComposeRuntime>();
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
        if (configuration.GetValue<long>("Runtime:Kubernetes:PodPidsLimit") <= 0
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
