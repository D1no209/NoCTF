using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Hosting.Health;

namespace NoCTF.Runner.Composition;

public sealed class RunnerProviderReadinessDependency(
    IOptions<RunnerOptions> options,
    RuntimeProviderAvailabilityCatalog probes,
    RunnerProviderHealthState providerHealth) : IReadinessDependency
{
    public string Name => "runtime-provider";
    public bool FailureIsCritical => true;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var provider = options.Value.Provider
            ?? throw new InvalidOperationException("Runner provider is not configured.");
        var probe = probes.Get(provider);
        await probe.CheckAvailabilityAsync(cancellationToken);
        providerHealth.EnsureReady(provider);
    }
}

public sealed class RuntimeProviderAvailabilityCatalog(
    IEnumerable<IRuntimeProviderAvailabilityProbe> probes)
{
    private readonly IReadOnlyDictionary<NoCTF.Domain.Runtime.RuntimeProvider,
        IRuntimeProviderAvailabilityProbe> probesByProvider = probes.ToDictionary(probe => probe.Provider);

    public IRuntimeProviderAvailabilityProbe Get(
        NoCTF.Domain.Runtime.RuntimeProvider provider) =>
        probesByProvider.TryGetValue(provider, out var probe)
            ? probe
            : throw new InvalidOperationException(
                $"Runtime provider '{provider}' has no availability probe.");
}
