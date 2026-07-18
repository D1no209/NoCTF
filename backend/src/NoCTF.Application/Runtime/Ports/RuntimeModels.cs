namespace NoCTF.Application.Runtime.Ports;

public sealed record ContainerResourceLimits(long MemoryBytes, long NanoCpus, long PidsLimit);
public sealed record ContainerSecurityPolicy(
    bool NoNewPrivileges,
    bool ReadonlyRootfs,
    bool RunAsNonRoot,
    IReadOnlyList<string> CapDrop,
    IReadOnlyList<string> CapAdd);

public sealed record ContainerRequest(
    Guid OperationId,
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
    string Provider,
    string ResourceId,
    string Status,
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
    Task<ContainerReceipt?> GetAsync(string resourceId, CancellationToken cancellationToken);
}

public interface IOneShotJobRunner
{
    Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken);
}
