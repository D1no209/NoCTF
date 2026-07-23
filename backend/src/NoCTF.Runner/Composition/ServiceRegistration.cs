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
            configuration["Runtime:Docker:PublicHost"] ?? "localhost");
        services.AddSingleton(options);
        services.AddSingleton<DockerContainerLifecycle>();
        services.AddSingleton<IRuntimeResourceReaper>(provider =>
            provider.GetRequiredService<DockerContainerLifecycle>());
        services.AddSingleton<DockerComposeRuntime>();
        services.AddSingleton(new KubernetesRuntimeOptions(
            configuration["Runtime:Kubernetes:Namespace"] ?? "noctf",
            configuration["Runtime:Kubernetes:PublicHost"] ?? "localhost",
            configuration["Runtime:Kubernetes:ImagePullPolicy"] ?? "IfNotPresent"));
        services.AddSingleton<IKubernetes>(_ => new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile()));
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<IRuntimeResourceReaper>(provider =>
            provider.GetRequiredService<KubernetesContainerLifecycle>());
        services.AddSingleton<KubernetesComposeRuntime>();
        services.AddSingleton<ILibvirtProcessAdapter, LibvirtProcessAdapter>();
        services.AddSingleton<LibvirtApplianceLifecycle>();
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
}
