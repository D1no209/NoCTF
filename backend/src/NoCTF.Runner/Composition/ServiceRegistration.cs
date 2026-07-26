using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using NoCTF.Infrastructure.Storage;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Libvirt;
using k8s;
using NoCTF.Runner.Messages;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Application.Authentication;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Runner.Composition;

public sealed class RunnerProgramMarker;

public static class ServiceRegistration
{
    public static IServiceCollection AddNoCtfRunner(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        var options = new DockerRuntimeOptions(
            configuration["Runtime:Docker:Endpoint"] ?? "npipe://./pipe/docker_engine",
            configuration["Runtime:Docker:Network"] ?? "noctf",
            configuration["Runtime:Docker:PublicHost"] ?? "localhost",
            configuration["Runtime:Docker:CallbackContainer"] ?? "noctf-awdp-callback",
            configuration["Runtime:Docker:CallbackContainerLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Docker:CallbackContainerLabelValue"] ?? "awdp-callback-gateway");
        services.AddSingleton(options);
        services.AddSingleton<DockerContainerLifecycle>();
        services.AddSingleton<IRuntimeResourceReaper>(provider =>
            provider.GetRequiredService<DockerContainerLifecycle>());
        services.AddSingleton<DockerComposeRuntime>();
        services.AddSingleton(new KubernetesRuntimeOptions(
            configuration["Runtime:Kubernetes:Namespace"] ?? "noctf",
            configuration["Runtime:Kubernetes:PublicHost"] ?? "localhost",
            configuration["Runtime:Kubernetes:ImagePullPolicy"] ?? "IfNotPresent",
            configuration["Runtime:Kubernetes:CallbackPodLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Kubernetes:CallbackPodLabelValue"] ?? "awdp-callback",
            ReadRequiredPositiveLong(configuration, "Runtime:Kubernetes:PodPidsLimit"),
            ReadRequiredString(configuration, "Runtime:Kubernetes:ClusterDomain"),
            ReadRequiredTrue(configuration, "Runtime:Kubernetes:NetworkPolicyRequired")));
        services.AddSingleton<IKubernetes>(_ =>
            new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()));
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<IRuntimeResourceReaper>(provider =>
            provider.GetRequiredService<KubernetesContainerLifecycle>());
        services.AddSingleton<IKomposeConverter>(new KomposeConverter());
        services.AddSingleton<KubernetesComposeRuntime>();
        var isLibvirtPool = string.Equals(
            configuration["Runner:Provider"],
            nameof(NoCTF.Domain.Runtime.RuntimeProvider.Libvirt),
            StringComparison.OrdinalIgnoreCase);
        var hasLibvirtConfiguration =
            !string.IsNullOrWhiteSpace(configuration["Runtime:Libvirt:PoolRoutedNetworkCidr"]);
        if (isLibvirtPool || hasLibvirtConfiguration)
        {
            services.AddSingleton(new LibvirtRuntimeOptions(
                ReadRequiredString(configuration, "Runtime:Libvirt:CacheDirectory"),
                ReadRequiredString(configuration, "Runtime:Libvirt:WorkDirectory"),
                ReadRequiredString(configuration, "Runtime:Libvirt:PoolRoutedNetworkCidr"),
                ReadRequiredString(configuration, "Runtime:Libvirt:NodeRoutedNetworkCidr"),
                ReadRequiredPositiveInt(
                    configuration,
                    "Runtime:Libvirt:RuntimeSubnetPrefixLength")));
            services.AddSingleton<ILibvirtProcessAdapter, LibvirtProcessAdapter>();
            services.AddHttpClient<OvaArtifactCache>();
            services.AddSingleton<LibvirtRoutedNetworkManager>();
            services.AddSingleton<LibvirtApplianceLifecycle>();
            services.AddSingleton<IOvaRuntime>(provider =>
                provider.GetRequiredService<LibvirtApplianceLifecycle>());
        }
        services.AddSingleton<RuntimeProviderCatalog>();
        services.AddSingleton<IOneShotRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton<IContainerRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton<IRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AwdFlagInjectionConfigurationCatalog>();
        services.AddSingleton<AwdCheckerConfigurationCatalog>();
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();
        services.AddSingleton<IAwdFlagInjectionExecutor, AwdFlagInjectionExecutor>();
        services.AddSingleton<IAwdFlagInjectionWorkReader, AwdFlagInjectionWorkReader>();
        services.AddSingleton<IAwdCheckerWorkReader, AwdCheckerWorkReader>();
        services.AddSingleton<IAwdCheckerExecutor, AwdCheckerExecutor>();
        services.AddSingleton<IAwdpFixWorkReader, AwdpFixWorkReader>();
        services.AddSingleton<IAwdpCheckerExecutor, AwdpCheckerExecutor>();
        services.AddSingleton<FixArchivePreparer>();
        services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        services.AddSingleton<IRuntimeNodeWorkReader, RuntimeNodeWorkReader>();
        return services;
    }

    private static long ReadRequiredPositiveLong(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        if (!long.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
            throw new InvalidOperationException(
                $"{key} must be configured as a positive integer.");
        return parsed;
    }

    private static int ReadRequiredPositiveInt(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
            throw new InvalidOperationException(
                $"{key} must be configured as a positive integer.");
        return parsed;
    }

    private static string ReadRequiredString(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} must be configured.");
        return value;
    }

    private static bool ReadRequiredTrue(
        IConfiguration configuration,
        string key)
    {
        if (!bool.TryParse(configuration[key], out var value) || !value)
            throw new InvalidOperationException($"{key} must be configured as true.");
        return true;
    }
}
