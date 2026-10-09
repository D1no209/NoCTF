using Docker.DotNet;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Docker.Containers;

public sealed class DockerExecutionIsolationProbe(DockerContainerLifecycle lifecycle, DockerRuntimeOptions options) : IRuntimeExecutionIsolationProbe
{
    public RuntimeProvider Provider => RuntimeProvider.Docker;
    public async Task<RuntimeIsolationState> CheckAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ExecutionNetworkName)) return RuntimeIsolationState.Unverified;
        try
        {
            await lifecycle.EnsureExecutionNetworkAsync(ct); return RuntimeIsolationState.Verified;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested
            && exception is DockerApiException or RuntimeConfigurationException or OperationCanceledException)
        { return RuntimeIsolationState.Unverified; }
    }
}
