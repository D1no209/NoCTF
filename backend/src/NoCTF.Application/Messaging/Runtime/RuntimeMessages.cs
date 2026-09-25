namespace NoCTF.Application.Messaging;

public sealed record DispatchRuntime(
    Guid RuntimeInstanceId,
    Guid DispatchAttemptId = default);
public sealed record StopRuntime(Guid RuntimeInstanceId);

public sealed record ReconcileRunnerAssignments(
    DateTimeOffset At,
    Guid? AfterRuntimeInstanceId = null);
