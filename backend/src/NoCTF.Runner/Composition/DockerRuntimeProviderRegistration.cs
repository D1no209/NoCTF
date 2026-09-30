using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Services;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runner.Composition;

internal static class DockerRuntimeProviderRegistration
{
    internal static IServiceCollection AddDockerRuntimeProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new DockerRuntimeOptions(
            configuration["Runtime:Docker:Endpoint"] ?? "npipe://./pipe/docker_engine",
            configuration["Runtime:Docker:Network"] ?? "noctf-challenges",
            configuration["Runtime:Docker:PublicHost"] ?? "localhost",
            configuration["Runtime:Docker:CallbackContainer"] ?? string.Empty,
            configuration["Runtime:Docker:CallbackContainerLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Docker:CallbackContainerLabelValue"] ?? "scoring-callback-gateway",
            configuration.GetValue<long?>("Runtime:Docker:RuntimeLogMaxSizeBytes")
                ?? 10_485_760,
            configuration.GetValue<int?>("Runtime:Docker:RuntimeLogMaxFiles") ?? 3,
            configuration.GetValue<int?>("Runtime:Docker:OneShotOutputLimitBytesPerStream")
                ?? 1_048_576,
            configuration["Runtime:Docker:ProxyContainer"] ?? string.Empty,
            configuration["Runtime:Docker:ProxyContainerLabelKey"]
                ?? "noctf.io/runtime-proxy-gateway",
            configuration["Runtime:Docker:ProxyContainerLabelValue"] ?? "true",
            CallbackNetworkName: configuration["Runtime:Docker:CallbackNetwork"] ?? "noctf-runtime-callback");
        if (options.RuntimeLogMaxSizeBytes <= 0
            || options.RuntimeLogMaxFiles <= 0
            || options.OneShotOutputLimitBytesPerStream <= 0
            || string.IsNullOrWhiteSpace(options.ProxyContainerLabelKey)
            || string.IsNullOrWhiteSpace(options.ProxyContainerLabelValue))
        {
            throw new InvalidOperationException(
                "Docker Runtime limits must be configured as positive integers.");
        }

        services.AddSingleton(options);
        services.AddSingleton<DockerContainerLifecycle>();
        services.AddSingleton<DockerContainerRuntime>();
        services.AddSingleton<DockerRuntimeResourceReconciler>();
        services.AddSingleton<IRuntimeProviderAvailabilityProbe>(provider =>
            provider.GetRequiredService<DockerRuntimeResourceReconciler>());
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<DockerRuntimeResourceReconciler>());
        return services;
    }
}
