using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record ClaimContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    ContainerRequest Definition) : IRunnerPoolMessage;

public sealed record ClaimComposeRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    ComposeRequest Definition) : IRunnerPoolMessage;

public sealed record ClaimOvaRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    OvaRuntimeRequest Definition) : IRunnerPoolMessage;

public sealed record ProvisionContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId,
    ContainerRequest Definition) : IRuntimeProvisionMessage;

public sealed record ProvisionComposeRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId,
    ComposeRequest Definition) : IRuntimeProvisionMessage;

public sealed record ProvisionOvaRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId,
    OvaRuntimeRequest Definition) : IRuntimeProvisionMessage;

public sealed record StopContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerPool,
    string RunnerId) : IRuntimeStopMessage;

public sealed record StopComposeRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerPool,
    string RunnerId) : IRuntimeStopMessage;

public sealed record StopOvaRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerPool,
    string RunnerId) : IRuntimeStopMessage;

public sealed record ReconcileLibvirtResources(
    string RunnerPool,
    string RunnerId,
    DateTimeOffset RequestedAt) : IRunnerNodeMessage;

public interface IRuntimeProvisionMessage : IRunnerNodeMessage
{
    Guid RuntimeInstanceId { get; }
    long ProcessingVersion { get; }
    int Generation { get; }
}

public interface IRuntimeStopMessage : IRunnerNodeMessage
{
    Guid RuntimeInstanceId { get; }
    long ProcessingVersion { get; }
}

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
        IRuntimeProvisionMessage message,
        CancellationToken cancellationToken);

    Task<RuntimeStopWork?> ReadStopAsync(
        IRuntimeStopMessage message,
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
