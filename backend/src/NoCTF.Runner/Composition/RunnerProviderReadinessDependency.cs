using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Hosting.Health;

namespace NoCTF.Runner.Composition;

public sealed class RunnerProviderReadinessDependency(
    IOptions<RunnerAvailabilityOptions> options,
    IServiceProvider services) : IReadinessDependency
{
    public string Name => "runtime-provider";
    public bool FailureIsCritical => true;

    public Task CheckAsync(CancellationToken cancellationToken)
    {
        var provider = options.Value.Provider
            ?? throw new InvalidOperationException("Runner provider is not configured.");
        var probe = services.GetKeyedService<IRuntimeProviderAvailabilityProbe>(provider)
            ?? throw new InvalidOperationException(
                $"Runtime provider '{provider}' has no availability probe.");
        return probe.CheckAvailabilityAsync(cancellationToken);
    }
}
