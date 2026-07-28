using NoCTF.Domain.Runtime;
using System.Text.Json.Serialization;

namespace NoCTF.Application.Runtime.Ports;

public sealed record RuntimeResourceLimits(long MemoryBytes, long NanoCpus, long PidsLimit);
public sealed record ContainerSecurityPolicy(
    bool NoNewPrivileges,
    bool ReadonlyRootfs,
    bool RunAsNonRoot,
    IReadOnlyList<string> CapDrop,
    IReadOnlyList<string> CapAdd);

public enum RuntimeAllocation
{
    Shared,
    PerTeam
}

public enum RuntimeExposure
{
    OwnerOnly,
    Participants
}

public enum RuntimeFlagSource
{
    Static,
    PerTeam,
    AwdRotation
}

public enum ContainerNetworkIsolation
{
    Shared,
    Isolated
}

public enum RuntimeEgressPolicy
{
    DenyAll,
    InternetOnly
}

public enum ContainerNetworkPurpose
{
    PersistentRuntime,
    AwdpVerification,
    AwdChecker
}

public sealed record RuntimeUrlBinding(
    string UrlTemplate,
    RuntimeExposure Exposure,
    int? ContainerPort = null,
    string? ServiceName = null,
    string? VmId = null,
    int? GuestPort = null);

public sealed record RuntimeInternalEndpointBinding(
    string UrlTemplate,
    int ContainerPort,
    string? ServiceName = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ContainerRuntimeDefinition), "container")]
[JsonDerivedType(typeof(ComposeRuntimeDefinition), "compose")]
[JsonDerivedType(typeof(OvaRuntimeDefinition), "ova")]
public abstract record ChallengeRuntimeDefinition
{
    [JsonIgnore]
    public abstract RuntimeKind RuntimeKind { get; }
}

public sealed record ContainerRuntimeDefinition(
    string Image,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyDictionary<int, int>? PortMappings = null,
    ContainerSecurityPolicy? Security = null,
    string? FlagEnvironmentVariableName = null,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.DenyAll) : ChallengeRuntimeDefinition
{
    public override RuntimeKind RuntimeKind => RuntimeKind.Container;
}

public sealed record ComposeRuntimeDefinition(
    string ComposeYaml,
    IReadOnlyDictionary<string, RuntimeResourceLimits> ServiceResources,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyDictionary<string, string>? FlagEnvironmentVariables = null,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.DenyAll)
    : ChallengeRuntimeDefinition
{
    public override RuntimeKind RuntimeKind => RuntimeKind.Compose;
}

public sealed record OvaRuntimeDefinition(
    string OvaSourceUrl,
    string Sha256) : ChallengeRuntimeDefinition
{
    public override RuntimeKind RuntimeKind => RuntimeKind.OvaVm;
}

public sealed record ChallengeRuntimeTemplate(
    RuntimeProvider Provider,
    RuntimeAllocation Allocation,
    ChallengeRuntimeDefinition Definition,
    RuntimeResourceLimits? Limits = null,
    int? TtlSeconds = null,
    int? OperationTimeoutSeconds = null,
    string RunnerPool = "default",
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null,
    RuntimeFlagSource FlagSource = RuntimeFlagSource.Static,
    RuntimeUrlBinding? ControlCheckUrlBinding = null)
{
    [JsonIgnore]
    public RuntimeKind RuntimeKind => Definition?.RuntimeKind
        ?? throw new InvalidOperationException("Runtime definition is required.");
}

public interface IChallengeRuntimeTemplateCatalog
{
    ChallengeRuntimeTemplate? Get(NoCTF.Domain.Competitions.GameMode mode, string challengeConfigurationJson);
}

public sealed record ContainerRequest(
    Guid OperationId,
    RuntimeProvider Provider,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<int, int> PortMappings,
    RuntimeResourceLimits Limits,
    ContainerSecurityPolicy Security,
    TimeSpan? Ttl,
    RunnerScoringCallback? ScoringCallback = null,
    string? NetworkName = null,
    TimeSpan? OperationTimeout = null,
    ContainerNetworkIsolation NetworkIsolation = ContainerNetworkIsolation.Shared,
    IReadOnlyList<int>? InternalPorts = null,
    bool AllowInternalCallback = false,
    int Generation = 0,
    Guid? RuntimeInstanceId = null,
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null,
    RuntimeUrlBinding? ControlCheckUrlBinding = null,
    RuntimeInternalEndpointBinding? AwdCheckerTargetBinding = null,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.DenyAll,
    ContainerNetworkPurpose NetworkPurpose = ContainerNetworkPurpose.PersistentRuntime)
{
    public IReadOnlyList<int> ContainerPorts =>
        [.. PortMappings.Keys.Concat(InternalPorts ?? []).Distinct().Order()];
}

/// <summary>Restricted metadata for a Runner-authenticated system-result callback.</summary>
public sealed record RunnerScoringCallback(
    Uri Url,
    string RunnerId,
    IReadOnlyDictionary<string, string> Context);

public sealed record ContainerReceipt(
    Guid OperationId,
    RuntimeProvider Provider,
    string ResourceId,
    RuntimeStatus Status,
    IReadOnlyDictionary<int, int> PortMappings,
    string? PublicHost,
    string? InternalHost,
    string? NetworkId = null,
    Guid? RuntimeInstanceId = null,
    int Generation = 0,
    string? IngressResourceId = null);

public sealed record OneShotResult(
    string ResourceId,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public interface IContainerLifecycle
{
    Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken);
    Task<ContainerReceipt> EnsureRunningAsync(
        ContainerRequest request,
        CancellationToken cancellationToken);
    Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken);
    Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken);
}

public interface IOneShotJobRunner
{
    Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken);
}

public sealed record ContainerExecResult(int ExitCode, bool TimedOut);

public interface IContainerSandboxLifecycle
{
    Task<string> CreateIsolatedNetworkAsync(
        ContainerNetworkPolicyRequest request,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
    Task DeleteIsolatedNetworkAsync(string networkId, CancellationToken cancellationToken);
    Task CopyArchiveAsync(ContainerReceipt receipt, Stream tarArchive, CancellationToken cancellationToken);
    Task<ContainerExecResult> ExecAsync(
        ContainerReceipt receipt, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken);
    Task<ContainerExecResult> ExecWithInputAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public readonly record struct RuntimeResourceIdentity(
    Guid RuntimeInstanceId,
    int Generation);

public sealed record ContainerNetworkPolicyRequest(
    RuntimeResourceIdentity Identity,
    ContainerNetworkPurpose Purpose,
    RuntimeEgressPolicy EgressPolicy,
    IReadOnlyList<int> PublicIngressPorts,
    int? TargetPort = null);

public sealed record RuntimeResourceReapResult(int RemovedCount, int FailedCount);

public interface IRuntimeResourceReaper
{
    RuntimeProvider Provider { get; }

    Task<RuntimeResourceReapResult> ReapExpiredAsync(
        DateTimeOffset now, CancellationToken cancellationToken);
}

public abstract record AttachedRuntimeTarget(
    RuntimeResourceIdentity Identity);

public sealed record AttachedContainerRuntimeTarget(
    RuntimeResourceIdentity Identity,
    ContainerReceipt Receipt)
    : AttachedRuntimeTarget(Identity);

public sealed record AttachedComposeRuntimeTarget(
    RuntimeResourceIdentity Identity,
    ComposeReceipt Receipt,
    string ServiceName)
    : AttachedRuntimeTarget(Identity);

public interface IAttachedOneShotJobRunner
{
    Task<OneShotResult> RunAttachedAsync(
        ContainerRequest request,
        AttachedRuntimeTarget target,
        CancellationToken cancellationToken);
}

public interface IRuntimeManagedResourceReconciler
{
    RuntimeProvider Provider { get; }

    Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken);

    Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken);
}
