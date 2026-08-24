using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record ProvisionContainerRuntime(
    Guid RuntimeInstanceId,
    string RunnerId,
    ContainerRequest Definition) : IRuntimeProvisionMessage;

public sealed record ProvisionComposeRuntime(
    Guid RuntimeInstanceId,
    string RunnerId,
    ComposeRequest Definition) : IRuntimeProvisionMessage;

public sealed record ProvisionOvaRuntime(
    Guid RuntimeInstanceId,
    string RunnerId,
    OvaRuntimeRequest Definition) : IRuntimeProvisionMessage;

public sealed record StopContainerRuntime(
    Guid RuntimeInstanceId,
    string RunnerId) : IRuntimeStopMessage;

public sealed record StopComposeRuntime(
    Guid RuntimeInstanceId,
    string RunnerId) : IRuntimeStopMessage;

public sealed record StopOvaRuntime(
    Guid RuntimeInstanceId,
    string RunnerId) : IRuntimeStopMessage;

public sealed record ReconcileRuntimeResources(
    string RunnerId,
    DateTimeOffset RequestedAt) : IRunnerNodeMessage;

public sealed record ForceTerminateRuntime(
    Guid RuntimeInstanceId,
    RuntimeProvider Provider,
    string RunnerId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset RequestedAt) : IRuntimeStopMessage;

public interface IRuntimeProvisionMessage : IRunnerNodeMessage
{
    Guid RuntimeInstanceId { get; }
}

public interface IRuntimeStopMessage : IRunnerNodeMessage
{
    Guid RuntimeInstanceId { get; }
}

public sealed record RuntimeStopWork(
    RuntimeProvider Provider,
    string? ProviderReceiptJson,
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
    string RunnerId,
    RuntimeProvider Provider,
    string ProviderReceiptJson,
    IReadOnlyList<string> Urls,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<RuntimePublishedPortMapping>? PublishedPorts = null);

public sealed record RuntimeProvisionFailed(
    Guid RuntimeInstanceId,
    RuntimeFailureCode FailureCode,
    string RunnerId);

public sealed record RuntimeProvisionCanceled(
    Guid RuntimeInstanceId,
    string RunnerId);

public sealed record RuntimeProvisionTerminated(
    Guid RuntimeInstanceId,
    string RunnerId,
    RuntimeFailureCode FailureCode);

public sealed record RuntimeStopped(
    Guid RuntimeInstanceId,
    string RunnerId);

public sealed record RuntimeStopFailed(
    Guid RuntimeInstanceId,
    string RunnerId,
    RuntimeFailureCode FailureCode);

public sealed record RuntimeForceTerminated(
    Guid RuntimeInstanceId,
    string RunnerId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    RuntimeCleanupResult CleanupResult);

public sealed record RuntimeForceTerminationFailed(
    Guid RuntimeInstanceId,
    string RunnerId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    RuntimeCleanupResult CleanupResult);
