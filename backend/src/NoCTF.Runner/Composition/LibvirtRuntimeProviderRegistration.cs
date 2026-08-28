using Microsoft.Extensions.Http.Resilience;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Libvirt;
using NoCTF.Runner.Messages;
using Polly;

namespace NoCTF.Runner.Composition;

internal static class LibvirtRuntimeProviderRegistration
{
    internal static IServiceCollection AddLibvirtRuntimeProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isActiveProvider)
    {
        var hasConfiguration = !string.IsNullOrWhiteSpace(
            configuration["Runtime:Libvirt:PoolRoutedNetworkCidr"]);
        if (!isActiveProvider && !hasConfiguration)
            return services;

        services.AddSingleton(new LibvirtRuntimeOptions(
            configuration["Runtime:Libvirt:CacheDirectory"] ?? string.Empty,
            configuration["Runtime:Libvirt:WorkDirectory"] ?? string.Empty,
            configuration["Runtime:Libvirt:PoolRoutedNetworkCidr"] ?? string.Empty,
            configuration["Runtime:Libvirt:NodeRoutedNetworkCidr"] ?? string.Empty,
            configuration.GetValue<int>("Runtime:Libvirt:RuntimeSubnetPrefixLength")));
        services.AddSingleton<ILibvirtProcessAdapter, LibvirtProcessAdapter>();
        services.AddHttpClient<OvaArtifactCache>(client =>
                client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("ova-artifact", pipeline =>
                pipeline.AddRetry(ServiceRegistration.CreateGetRetry(maxRetryAttempts: 2)));
        services.AddSingleton<LibvirtRoutedNetworkManager>();
        services.AddSingleton<LibvirtApplianceLifecycle>();
        services.AddSingleton<IOvaRuntime>(provider =>
            provider.GetRequiredService<LibvirtApplianceLifecycle>());
        services.AddSingleton<LibvirtRuntimeResourceReconciler>();
        services.AddSingleton<IRuntimeProviderAvailabilityProbe>(provider =>
            provider.GetRequiredService<LibvirtRuntimeResourceReconciler>());
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<LibvirtRuntimeResourceReconciler>());
        return services;
    }
}
