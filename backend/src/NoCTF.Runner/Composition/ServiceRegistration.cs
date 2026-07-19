using FastEndpoints;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Authentication;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using k8s;

namespace NoCTF.Runner.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddNoCtfRunner(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new DockerRuntimeOptions(
            configuration["Runtime:Docker:Endpoint"] ?? "npipe://./pipe/docker_engine",
            configuration["Runtime:Docker:Network"] ?? "noctf",
            configuration["Runtime:Docker:PublicHost"] ?? "localhost");
        services.AddSingleton(options);
        services.AddSingleton<DockerContainerLifecycle>();
        services.AddSingleton<DockerComposeRuntime>();
        services.AddSingleton(new KubernetesRuntimeOptions(
            configuration["Runtime:Kubernetes:Namespace"] ?? "noctf",
            configuration["Runtime:Kubernetes:PublicHost"] ?? "localhost",
            configuration["Runtime:Kubernetes:ImagePullPolicy"] ?? "IfNotPresent"));
        services.AddSingleton<IKubernetes>(_ => new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile()));
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<KubernetesComposeRuntime>();
        services.AddSingleton<RuntimeProviderCatalog>();
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();
        services.AddFastEndpoints();
        return services;
    }
}
