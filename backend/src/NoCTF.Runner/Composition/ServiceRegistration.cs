using FastEndpoints;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;

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
        services.AddSingleton<IContainerLifecycle>(sp => sp.GetRequiredService<DockerContainerLifecycle>());
        services.AddSingleton<IOneShotJobRunner>(sp => sp.GetRequiredService<DockerContainerLifecycle>());
        services.AddSingleton<IComposeRuntime>(new DockerComposeRuntime());
        services.AddFastEndpoints();
        return services;
    }
}
