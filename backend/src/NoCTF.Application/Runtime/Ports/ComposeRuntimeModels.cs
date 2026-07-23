using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Ports;

public sealed record ComposeRequest(
    Guid OperationId,
    string ProjectName,
    string ComposeYaml,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> Labels,
    TimeSpan? Ttl);

public sealed record ComposeServiceStatus(
    string Name,
    string ResourceId,
    RuntimeStatus Status,
    IReadOnlyDictionary<int, int> PublishedPorts,
    string? InternalHost);

public sealed record ComposeReceipt(Guid OperationId, RuntimeProvider Provider, string ProjectName, string Namespace, DateTimeOffset CreatedAt);
public sealed record ComposeStatus(string ProjectName, RuntimeStatus Status, IReadOnlyList<ComposeServiceStatus> Services);

public interface IComposeRuntime
{
    Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken);
    Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken);
    Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken);
    Task<ContainerExecResult> ExecAsync(
        ComposeReceipt receipt,
        string serviceName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
