using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Provisioning;

public sealed record ComposeRequest(
    Guid OperationId,
    RuntimeProvider Provider,
    string ProjectName,
    string ComposeYaml,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, RuntimeResourceLimits> ServiceResources,
    RuntimeResourceLimits Limits,
    TimeSpan? Ttl,
    TimeSpan OperationTimeout,
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null,
    RuntimeUrlBinding? ControlCheckUrlBinding = null,
    RuntimeInternalEndpointBinding? AwdCheckerTargetBinding = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ServiceEnvironment = null,
    RuntimeEgressPolicy EgressPolicy = RuntimeEgressPolicy.Isolated,
    IReadOnlyList<RuntimePublishedPortMapping>? PublishedPorts = null);

public sealed record ComposeServiceStatus(
    string Name,
    string ResourceId,
    RuntimeStatus Status,
    IReadOnlyDictionary<int, int> PublishedPorts,
    string? InternalHost);

public sealed record ComposeReceipt(
    Guid OperationId,
    RuntimeProvider Provider,
    string ProjectName,
    string Namespace,
    string PublicHost,
    DateTimeOffset CreatedAt);
public sealed record ComposeStatus(string ProjectName, RuntimeStatus Status, IReadOnlyList<ComposeServiceStatus> Services);

public interface IComposeRuntime
{
    Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken);
    Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken);
    Task DownAsync(
        ComposeReceipt receipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) =>
        DownAsync(receipt, cancellationToken);
    Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken);
    Task<ContainerExecResult> ExecAsync(
        ComposeReceipt receipt,
        string serviceName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
