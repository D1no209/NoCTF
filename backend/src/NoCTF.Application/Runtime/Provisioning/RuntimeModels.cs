using NoCTF.Domain.Runtime;
using System.Text.Json.Serialization;

namespace NoCTF.Application.Runtime.Provisioning;

public sealed record RuntimeResourceLimits(long MemoryBytes, long CpuMillicores, long PidsLimit);
public sealed class RuntimeConfigurationException(string message) : Exception(message);

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

public enum RuntimeEgressPolicy
{
    Isolated,
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

public sealed record RuntimeInternalEndpointBinding(string? ServiceName = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ContainerRuntimeDefinition), "container")]
[JsonDerivedType(typeof(OvaRuntimeDefinition), "ova")]
public abstract record ChallengeRuntimeDefinition
{
    [JsonIgnore]
    public abstract RuntimeKind RuntimeKind { get; }
}

public sealed record ContainerRuntimeDefinition(
    IReadOnlyList<RuntimeServiceDefinition> Services,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.Isolated) : ChallengeRuntimeDefinition
{
    public override RuntimeKind RuntimeKind => RuntimeKind.Container;
}

public sealed record OvaRuntimeDefinition(
    string OvaSourceUrl,
    string Sha256) : ChallengeRuntimeDefinition
{
    public override RuntimeKind RuntimeKind => RuntimeKind.OvaVm;
}

public sealed record ChallengeRuntimeTemplate(
    RuntimeAllocation Allocation,
    ChallengeRuntimeDefinition Definition,
    RuntimeResourceLimits? Limits = null,
    int? TtlSeconds = null,
    int? OperationTimeoutSeconds = null,
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
    ChallengeRuntimeTemplate? Get(NoCTF.Domain.Challenges.ChallengeDefinition? definition);
}

public sealed record RuntimePlacement(RuntimeProvider Provider, string RunnerPool);

public sealed record RuntimePublishedPortMapping(
    string? ServiceName,
    int ContainerPort,
    int HostPort);

public sealed record RuntimeAccessEndpointMapping(
    int BindingIndex,
    string? DirectAddress,
    string? TargetHost,
    int? TargetPort);

/// <summary>Resolves platform-owned Runtime placement independently of challenge definitions.</summary>
public interface IRuntimePlacementPolicy
{
    RuntimePlacement Resolve(RuntimeKind runtimeKind);
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
    TimeSpan? Ttl,
    RunnerScoringCallback? ScoringCallback = null,
    string? NetworkName = null,
    TimeSpan? OperationTimeout = null,
    IReadOnlyList<int>? InternalPorts = null,
    bool AllowInternalCallback = false,
    Guid? RuntimeInstanceId = null,
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null,
    RuntimeUrlBinding? ControlCheckUrlBinding = null,
    RuntimeInternalEndpointBinding? AwdCheckerTargetBinding = null,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.Isolated,
    ContainerNetworkPurpose NetworkPurpose = ContainerNetworkPurpose.PersistentRuntime,
    RuntimeResourceLimits? Budget = null,
    RuntimeAccessMode AccessMode = RuntimeAccessMode.Direct,
    IReadOnlyList<string>? Arguments = null,
    string? ServiceName = null,
    bool RegisterServiceAlias = false,
    string? DiscoveryServiceName = null)
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
    Guid? RuntimeInstanceId = null);

public sealed record OneShotResult(
    string ResourceId,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public sealed record OneShotInputArchive(Stream Archive, string DestinationPath)
{
    public const string RootDestinationPath = "/";
    private int preparationCompleted;

    public bool PreparationCompleted => Volatile.Read(ref preparationCompleted) != 0;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Archive);
        if (!Archive.CanRead)
            throw new ArgumentException("The one-shot input archive must be readable.", nameof(Archive));
        if (!string.Equals(DestinationPath, RootDestinationPath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The one-shot input destination must be '{RootDestinationPath}'.",
                nameof(DestinationPath));
        }
    }

    public void MarkPreparationCompleted() =>
        Interlocked.Exchange(ref preparationCompleted, 1);

    public override string ToString() =>
        $"OneShotInputArchive {{ Archive = [REDACTED], DestinationPath = /, "
        + $"PreparationCompleted = {PreparationCompleted} }}";
}

public sealed class OneShotInputPreparationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class OneShotCleanupException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public enum RuntimeTerminationMode
{
    GracefulThenForce,
    Force
}

public sealed record RuntimeTerminationPolicy(
    TimeSpan GracefulStopTimeout,
    TimeSpan ForceDeleteTimeout,
    TimeSpan NetworkCleanupTimeout,
    TimeSpan VerificationTimeout)
{
    public static RuntimeTerminationPolicy Default { get; } = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(3));
}

public interface IContainerLifecycle
{
    Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken);
    Task<ContainerReceipt> EnsureRunningAsync(
        ContainerRequest request,
        CancellationToken cancellationToken);
    Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken);
    Task DestroyAsync(
        ContainerReceipt receipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) =>
        DestroyAsync(receipt, cancellationToken);
    Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken);
}

public interface IOneShotJobRunner
{
    Task<OneShotResult> RunAsync(
        ContainerRequest request,
        OneShotInputArchive? input,
        CancellationToken cancellationToken);
}

public sealed record ContainerExecResult(int ExitCode, bool TimedOut);

public interface IContainerSandboxLifecycle
{
    Task<string> CreateIsolatedNetworkAsync(
        ContainerNetworkPolicyRequest request,
        CancellationToken cancellationToken);
    Task DeleteIsolatedNetworkAsync(string networkId, CancellationToken cancellationToken);
    Task<bool> IsolatedNetworkExistsAsync(string networkId, CancellationToken cancellationToken);
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

public readonly record struct RuntimeResourceIdentity(Guid RuntimeInstanceId);

public sealed record ContainerNetworkPolicyRequest(
    RuntimeResourceIdentity Identity,
    ContainerNetworkPurpose Purpose,
    RuntimeEgressPolicy EgressPolicy,
    IReadOnlyList<int> PublicIngressPorts,
    int? TargetPort = null,
    IReadOnlyList<int>? ProxyIngressPorts = null);

public abstract record AttachedRuntimeTarget(
    RuntimeResourceIdentity Identity);

public sealed record AttachedContainerRuntimeTarget(
    RuntimeResourceIdentity Identity,
    ContainerReceipt Receipt)
    : AttachedRuntimeTarget(Identity);

public interface IAttachedOneShotJobRunner
{
    Task<OneShotResult> RunAttachedAsync(ContainerRequest request, AttachedRuntimeTarget target, CancellationToken cancellationToken);
    Task<OneShotResult> RunAttachedAsync(ContainerRequest request, AttachedRuntimeTarget target, OneShotInputArchive? input, CancellationToken cancellationToken) => input is null
        ? RunAttachedAsync(request, target, cancellationToken)
        : throw new NotSupportedException("This job runner cannot prepare an input archive.");
}

public interface IRuntimeManagedResourceReconciler
{
    RuntimeProvider Provider { get; }

    Task<bool?> WorkloadExistsAsync(RuntimeWorkloadIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult<bool?>(null);

    Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken);

    Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken);

    Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) =>
        DestroyByIdentityAsync(identity, cancellationToken);
}

public interface IRuntimeProxyNetworkReconciler
{
    Task EnsureProxyNetworkAsync(
        Guid runtimeInstanceId,
        RuntimeKind runtimeKind,
        RuntimeReceiptData providerReceipt,
        CancellationToken cancellationToken);
}

public interface IRuntimeProviderAvailabilityProbe
{
    RuntimeProvider Provider { get; }

    Task CheckAvailabilityAsync(CancellationToken cancellationToken);
}
