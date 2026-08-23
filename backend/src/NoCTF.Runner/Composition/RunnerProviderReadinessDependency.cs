using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Hosting.Health;

namespace NoCTF.Runner.Composition;

public sealed class RunnerProviderReadinessDependency(
    IOptions<RunnerOptions> options,
    IServiceProvider services,
    RunnerProviderHealthState providerHealth) : IReadinessDependency
{
    public string Name => "runtime-provider";
    public bool FailureIsCritical => true;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var provider = options.Value.Provider
            ?? throw new InvalidOperationException("Runner provider is not configured.");
        var probe = services.GetKeyedService<IRuntimeProviderAvailabilityProbe>(provider)
            ?? throw new InvalidOperationException(
                $"Runtime provider '{provider}' has no availability probe.");
        await probe.CheckAvailabilityAsync(cancellationToken);
        providerHealth.EnsureReady(provider);
    }
}
