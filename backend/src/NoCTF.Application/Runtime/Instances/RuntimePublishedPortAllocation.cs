using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public enum RuntimePublishedPortAllocationFailure
{
    RangeExhausted,
    TargetMismatch
}

public sealed record RuntimePublishedPortAllocationResult(
    IReadOnlyList<RuntimePublishedPortMapping> Mappings,
    RuntimePublishedPortAllocationFailure? Failure = null);

public interface IRuntimePublishedPortAllocator
{
    Task<RuntimePublishedPortAllocationResult> AllocateAsync(
        RuntimeInstance instance,
        IReadOnlyList<RuntimePublishedPortTarget> targets,
        DateTimeOffset allocatedAt,
        CancellationToken cancellationToken);
}

public static class RuntimePublishedPortClaims
{
    public static IReadOnlyList<RuntimePublishedPortTarget> Targets(IRunnerPoolMessage claim) =>
        claim switch
        {
            ClaimContainerRuntime container => container.Definition.PortMappings.Keys
                .Order()
                .Select(port => new RuntimePublishedPortTarget(null, port))
                .ToArray(),
            ClaimComposeRuntime compose => ComposeTargets(compose.Definition),
            _ => []
        };

    public static IRunnerPoolMessage Apply(
        IRunnerPoolMessage claim,
        IReadOnlyList<RuntimePublishedPortMapping> mappings) =>
        claim switch
        {
            ClaimContainerRuntime container => container with
            {
                Definition = container.Definition with
                {
                    PortMappings = mappings.ToDictionary(
                        mapping => mapping.ContainerPort,
                        mapping => mapping.HostPort)
                }
            },
            ClaimComposeRuntime compose => compose with
            {
                Definition = compose.Definition with { PublishedPorts = mappings }
            },
            _ when mappings.Count == 0 => claim,
            _ => throw new InvalidOperationException(
                "Only Container and Compose claims can publish host ports.")
        };

    private static IReadOnlyList<RuntimePublishedPortTarget> ComposeTargets(
        ComposeRequest request) =>
        (request.UrlBindings ?? [])
        .Append(request.ControlCheckUrlBinding)
        .Where(binding => binding?.ServiceName is not null && binding.ContainerPort is not null)
        .Select(binding => new RuntimePublishedPortTarget(
            binding!.ServiceName,
            binding.ContainerPort!.Value))
        .Distinct()
        .OrderBy(target => target.ServiceName, StringComparer.Ordinal)
        .ThenBy(target => target.ContainerPort)
        .ToArray();
}
