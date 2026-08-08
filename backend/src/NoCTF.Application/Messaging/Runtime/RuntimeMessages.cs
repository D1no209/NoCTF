namespace NoCTF.Application.Messaging;

public sealed record DispatchRuntime(Guid RuntimeInstanceId, long ProcessingVersion);
public sealed record StopRuntime(Guid RuntimeInstanceId, long ProcessingVersion);

public sealed record ReleaseRunnerCapacity(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerPool,
    string RunnerId,
    Guid AssignmentReleaseToken);

public sealed record ReconcileRunnerAssignments(
    DateTimeOffset At,
    Guid? AfterRuntimeInstanceId = null);
