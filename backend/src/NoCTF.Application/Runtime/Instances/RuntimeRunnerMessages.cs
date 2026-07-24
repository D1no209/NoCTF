using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record ClaimContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    ContainerRequest Definition) : IRunnerPoolMessage;

public sealed record ProvisionContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId,
    ContainerRequest Definition) : IRunnerNodeMessage;

public sealed record StopContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record RuntimeStopWork(
    RuntimeProvider Provider,
    string ProviderReceiptJson);

public enum RuntimeProvisionWorkStatus
{
    Current,
    AssignmentRetained,
    AssignmentAbsent
}

public interface IRuntimeNodeWorkReader
{
    Task<RuntimeProvisionWorkStatus> ReadProvisionStatusAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken);

    Task<RuntimeStopWork?> ReadStopAsync(
        StopContainerRuntime message,
        CancellationToken cancellationToken);
}

public sealed record RuntimeProvisioned(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerId,
    RuntimeProvider Provider,
    string ProviderReceiptJson,
    IReadOnlyList<string> Urls,
    IReadOnlyList<int> ParticipantUrlIndexes,
    DateTimeOffset? ExpiresAt,
    string? ControlCheckUrl = null);

public sealed record RuntimeProvisionFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    RuntimeFailureCode FailureCode,
    string? RunnerId = null);

public sealed record RuntimeStopped(Guid RuntimeInstanceId, long ProcessingVersion);
public sealed record RuntimeStopFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    RuntimeFailureCode FailureCode);
