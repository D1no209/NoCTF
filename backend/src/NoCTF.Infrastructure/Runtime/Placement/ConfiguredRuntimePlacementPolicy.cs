using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Runtime.Placement;

/// <summary>Chooses the deployment-wide Runtime provider and Runner pool.</summary>
public sealed class ConfiguredRuntimePlacementPolicy : IRuntimePlacementPolicy
{
    private readonly RuntimePlacement containerPlacement;

    public ConfiguredRuntimePlacementPolicy(IOptions<RuntimePlacementOptions> configuredOptions)
    {
        var options = configuredOptions.Value;
        containerPlacement = new(options.Provider, options.RunnerPool);
    }

    public RuntimePlacement Resolve(RuntimeKind runtimeKind) => runtimeKind switch
    {
        RuntimeKind.Container => containerPlacement,
        RuntimeKind.OvaVm => throw new InvalidOperationException(
            "The configured platform supports only Container runtimes."),
        _ => throw new ArgumentOutOfRangeException(nameof(runtimeKind), runtimeKind, null)
    };
}

public sealed class RuntimePlacementOptions
{
    public const string SectionName = "Runtime";

    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Docker;

    public string RunnerPool { get; set; } = "default";
}
