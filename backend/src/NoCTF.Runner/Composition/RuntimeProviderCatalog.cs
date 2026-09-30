using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Services;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Services;
using NoCTF.Runtime.Kubernetes.Containers;

namespace NoCTF.Runner.Composition;

public interface IOneShotRuntimeProviderCatalog
{
    IOneShotJobRunner OneShot(RuntimeProvider provider);
    IAttachedOneShotJobRunner Attached(RuntimeProvider provider);
}

public interface IContainerRuntimeProviderCatalog
{
    IContainerLifecycle Containers(RuntimeProvider provider);
}

public interface IRuntimeProviderCatalog : IContainerRuntimeProviderCatalog
{
    IContainerSandboxLifecycle Sandbox(RuntimeProvider provider);
    IContainerRuntime Runtime(RuntimeProvider provider);
    IOvaRuntime Appliance(RuntimeProvider provider);
}

public sealed class RuntimeProviderCatalog(
    DockerContainerLifecycle dockerContainers,
    KubernetesContainerLifecycle kubernetesContainers,
    DockerContainerRuntime dockerRuntime,
    KubernetesContainerRuntime kubernetesRuntime,
    IEnumerable<IOvaRuntime> appliances)
    : IOneShotRuntimeProviderCatalog, IRuntimeProviderCatalog
{
    public IContainerLifecycle Containers(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => dockerContainers,
        RuntimeProvider.Kubernetes => kubernetesContainers,
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IContainerRuntime Runtime(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => dockerRuntime,
        RuntimeProvider.Kubernetes => kubernetesRuntime,
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IOvaRuntime Appliance(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Libvirt => appliances.SingleOrDefault()
            ?? throw new UnsupportedRuntimeProviderException(provider),
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => dockerContainers,
        RuntimeProvider.Kubernetes => kubernetesContainers,
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IOneShotJobRunner OneShot(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => dockerContainers,
        RuntimeProvider.Kubernetes => kubernetesContainers,
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };

    public IAttachedOneShotJobRunner Attached(RuntimeProvider provider) => provider switch
    {
        RuntimeProvider.Docker => dockerContainers,
        RuntimeProvider.Kubernetes => kubernetesContainers,
        _ => throw new UnsupportedRuntimeProviderException(provider)
    };
}

public sealed class UnsupportedRuntimeProviderException(RuntimeProvider provider)
    : Exception($"Runtime provider '{provider}' is not available for this operation.");
