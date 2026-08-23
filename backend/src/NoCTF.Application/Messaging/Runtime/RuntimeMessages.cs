namespace NoCTF.Application.Messaging;

public sealed record DispatchRuntime(Guid RuntimeInstanceId);
public sealed record StopRuntime(Guid RuntimeInstanceId);

public sealed record ReleaseRunnerCapacity(
    Guid RuntimeInstanceId,
    string RunnerPool,
    string RunnerId,
    Guid AssignmentReleaseToken);

public sealed record ReconcileRunnerAssignments(
    DateTimeOffset At,
    Guid? AfterRuntimeInstanceId = null);
