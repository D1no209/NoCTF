using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Ports;

public sealed record ContainerResourceLimits(long MemoryBytes, long NanoCpus, long PidsLimit);
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

public sealed record ChallengeRuntimeTemplate(
    RuntimeProvider Provider,
    RuntimeAllocation Allocation,
    string Image,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyDictionary<int, int>? PortMappings = null,
    ContainerResourceLimits? Limits = null,
    ContainerSecurityPolicy? Security = null,
    int? TtlSeconds = null,
    int? OperationTimeoutSeconds = null);

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
    ContainerResourceLimits Limits,
    ContainerSecurityPolicy Security,
    TimeSpan? Ttl);

public sealed record ContainerReceipt(
    Guid OperationId,
    RuntimeProvider Provider,
    string ResourceId,
    RuntimeStatus Status,
    IReadOnlyDictionary<int, int> PortMappings,
    string? PublicHost,
    string? InternalHost);

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
    Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken);
    Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken);
}

public interface IOneShotJobRunner
{
    Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken);
}
