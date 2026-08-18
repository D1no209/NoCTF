using NoCTF.Application.Runtime.Provisioning;
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
    int Generation,
    string RunnerPool,
    string RunnerId) : IRuntimeStopMessage;

public sealed record StopComposeRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId) : IRuntimeStopMessage;

public sealed record StopOvaRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId) : IRuntimeStopMessage;

public sealed record ReconcileRuntimeResources(
    string RunnerPool,
    string RunnerId,
    DateTimeOffset RequestedAt) : IRunnerNodeMessage;

public sealed record ForceTerminateRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    RuntimeProvider Provider,
    string RunnerPool,
    string RunnerId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset RequestedAt) : IRuntimeStopMessage;

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
    int Generation { get; }
}

public sealed record RuntimeStopWork(
    RuntimeProvider Provider,
    string? ProviderReceiptJson,
    int Generation = 0,
    RuntimeKind RuntimeKind = RuntimeKind.Container);

public enum RuntimeProvisionWorkStatus
{
    Current,
    StopRequested,
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
    int Generation,
    string RunnerId,
    RuntimeProvider Provider,
    string ProviderReceiptJson,
    IReadOnlyList<string> Urls,
    IReadOnlyList<int> ParticipantUrlIndexes,
    DateTimeOffset? ExpiresAt,
    string? ControlCheckUrl = null,
    string? AwdCheckerTargetHost = null,
    IReadOnlyList<RuntimePublishedPortMapping>? PublishedPorts = null);

public sealed record RuntimeProvisionFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    RuntimeFailureCode FailureCode,
    string RunnerId);

public sealed record RuntimeProvisionCanceled(
    Guid RuntimeInstanceId,
    long ProvisionProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId);

public sealed record RuntimeProvisionTerminated(
    Guid RuntimeInstanceId,
    long ProvisionProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId,
    RuntimeFailureCode FailureCode);

public sealed record RuntimeStopped(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId);
public sealed record RuntimeStopFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    string RunnerId,
    RuntimeFailureCode FailureCode);

public sealed record RuntimeForceTerminated(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    RuntimeCleanupResult CleanupResult);

public sealed record RuntimeForceTerminationFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    RuntimeCleanupResult CleanupResult);
