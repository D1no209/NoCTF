using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Runtime.Placement;

/// <summary>Chooses the deployment-wide Runtime provider and Runner pool.</summary>
public sealed class ConfiguredRuntimePlacementPolicy : IRuntimePlacementPolicy
{
    private readonly RuntimePlacement containerPlacement;

    public ConfiguredRuntimePlacementPolicy(IConfiguration configuration)
    {
        var providerText = configuration["Runtime:Provider"]
            ?? configuration["Runner:Provider"]
            ?? nameof(RuntimeProvider.Docker);
        if (!Enum.TryParse<RuntimeProvider>(providerText, true, out var provider)
            || provider is not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes))
        {
            throw new InvalidOperationException(
                "Runtime:Provider must be Docker or Kubernetes.");
        }

        var runnerPool = configuration["Runtime:RunnerPool"]
            ?? configuration["Runner:Pool"]
            ?? "default";
        if (string.IsNullOrWhiteSpace(runnerPool) || runnerPool.Length > 256)
            throw new InvalidOperationException(
                "Runtime:RunnerPool must contain 1..256 characters.");

        containerPlacement = new(provider, runnerPool);
    }

    public RuntimePlacement Resolve(RuntimeKind runtimeKind) => runtimeKind switch
    {
        RuntimeKind.Container or RuntimeKind.Compose => containerPlacement,
        RuntimeKind.OvaVm => throw new InvalidOperationException(
            "The configured platform supports only Container and Compose runtimes."),
        _ => throw new ArgumentOutOfRangeException(nameof(runtimeKind), runtimeKind, null)
    };
}
