using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.SystemProducers;

namespace NoCTF.Infrastructure.Runtime;

public sealed class UnavailableAwdFlagInjector : IAwdFlagInjector
{
    public Task<ContainerExecResult> InjectAsync(
        ContainerReceipt runtime,
        AwdFlagInjectionSettings settings,
        string flag,
        CancellationToken cancellationToken) =>
        Task.FromException<ContainerExecResult>(
            new InvalidOperationException("AWD flag injection requires the Runner service."));
}
