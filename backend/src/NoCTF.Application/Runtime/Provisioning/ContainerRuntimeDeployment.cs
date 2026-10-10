using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Provisioning;

/// <summary>A provider-neutral named service, using image defaults unless overridden.</summary>
public sealed record RuntimeServiceDefinition(
    string Name,
    string Image,
    decimal CpuCores = 0.5m,
    long MemoryMiB = 512,
    IReadOnlyList<string>? Command = null,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyList<int>? InternalPorts = null,
    string? FlagEnvironmentVariableName = null)
{
    /// <summary>Converts authoring units to exact resource admission units.</summary>
    public RuntimeResourceLimits Resources(long processLimit) => new(
        checked(MemoryMiB * 1024 * 1024),
        checked((long)(CpuCores * 1000)),
        processLimit);
}

/// <summary>The platform-generated executable plan for an entire container Runtime.</summary>
public sealed record ContainerRuntimeRequest(
    Guid OperationId,
    RuntimeProvider Provider,
    IReadOnlyList<RuntimeServiceDefinition> Services,
    IReadOnlyDictionary<string, string> Labels,
    RuntimeResourceLimits Limits,
    TimeSpan? Ttl,
    TimeSpan OperationTimeout,
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null,
    RuntimeUrlBinding? ControlCheckUrlBinding = null,
    RuntimeInternalEndpointBinding? AwdCheckerTargetBinding = null,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.Isolated,
    RuntimeAccessMode AccessMode = RuntimeAccessMode.Direct,
    ContainerNetworkPurpose Purpose = ContainerNetworkPurpose.PersistentRuntime,
    bool AllowInternalCallback = false,
    Guid? ExecutionScopeId = null)
{
    public string ProjectName => $"noctf-rt-{OperationId:N}";
    public IReadOnlyDictionary<string, RuntimeResourceLimits> ServiceResources =>
        Services.ToDictionary(service => service.Name, service => service.Resources(Limits.PidsLimit / Services.Count), StringComparer.Ordinal);
}

/// <summary>The current resource and address for a named service.</summary>
public sealed record ContainerServiceStatus(
    string Name,
    string ResourceId,
    RuntimeStatus Status,
    IReadOnlyDictionary<int, int> PublishedPorts,
    string? InternalHost);

/// <summary>Only instance-owned resources are recorded as cleanup targets.</summary>
public sealed record ContainerDeploymentReceipt(
    Guid OperationId,
    RuntimeProvider Provider,
    string ProjectName,
    string Namespace,
    string PublicHost,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ContainerServiceStatus> Services,
    string? OwnedNetworkId = null,
    string? DiscoveryServiceName = null,
    Guid? ExecutionScopeId = null,
    RuntimeIsolationState IsolationState = RuntimeIsolationState.Unverified);

public sealed record ContainerRuntimeStatus(string ProjectName, RuntimeStatus Status, IReadOnlyList<ContainerServiceStatus> Services);

/// <summary>Orchestrates one or more named services without provider-specific authoring syntax.</summary>
public interface IContainerRuntime
{
    Task<ContainerDeploymentReceipt> UpAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken);
    Task DownAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken);
    Task DownAsync(ContainerDeploymentReceipt receipt, RuntimeTerminationMode mode, RuntimeTerminationPolicy policy, CancellationToken cancellationToken) => DownAsync(receipt, cancellationToken);
    Task<ContainerRuntimeStatus?> GetStatusAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken);
    Task<ContainerExecResult> ExecAsync(ContainerDeploymentReceipt receipt, string serviceName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Deployment-owned process limits used by admission and providers.</summary>
public sealed class RuntimeExecutionOptions
{
    public long ProcessesPerService { get; set; } = 256;
}
