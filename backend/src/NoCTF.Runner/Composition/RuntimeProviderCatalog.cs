using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Containers;

namespace NoCTF.Runner.Composition;

public interface IOneShotRuntimeProviderCatalog
{
    IOneShotJobRunner OneShot(RuntimeProvider provider);
}

public interface IContainerRuntimeProviderCatalog
{
    IContainerLifecycle Containers(RuntimeProvider provider);
}

public sealed class RuntimeProviderCatalog(IServiceProvider services)
    : IOneShotRuntimeProviderCatalog, IContainerRuntimeProviderCatalog
{
    public IContainerLifecycle Containers(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => services.GetRequiredService<DockerContainerLifecycle>(),
        RuntimeProvider.Kubernetes => services.GetRequiredService<KubernetesContainerLifecycle>(),
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IComposeRuntime Compose(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => services.GetRequiredService<DockerComposeRuntime>(),
        RuntimeProvider.Kubernetes => services.GetRequiredService<KubernetesComposeRuntime>(),
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IOneShotJobRunner OneShot(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => services.GetRequiredService<DockerContainerLifecycle>(),
        RuntimeProvider.Kubernetes => services.GetRequiredService<KubernetesContainerLifecycle>(),
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };
}

public sealed class UnsupportedRuntimeProviderException(RuntimeProvider provider)
    : Exception($"Runtime provider '{provider}' is not available for this operation.");
