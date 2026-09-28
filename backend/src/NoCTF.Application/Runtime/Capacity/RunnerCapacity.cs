using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public enum RunnerCapacityAvailability
{
    Claimed,
    Insufficient,
    Unavailable
}

public enum RunnerHeartbeatStatus
{
    Online,
    Offline,
    Unavailable
}

public enum RunnerCapacityClaimState
{
    Acquired,
    AlreadyOwned
}

public enum RunnerCapacityReleaseOutcome
{
    Released,
    AlreadyReleased,
    OwnerMismatch,
    RecoveryRequired
}

public enum RunnerPoolInventoryAvailability
{
    Available,
    Unavailable
}

public sealed record RunnerPoolInventory(
    RunnerPoolInventoryAvailability Availability,
    IReadOnlyList<string> RunnerIds);

public sealed record RunnerCapacityRequest(
    Guid RuntimeInstanceId,
    string Pool,
    long MemoryBytes,
    long NanoCpus,
    long PidsLimit,
    RuntimeWorkloadIdentity? Workload = null,
    Guid? GameplayFactId = null,
    RuntimeResourceAmount? Limit = null)
{
    public string ClaimSuffix => Workload?.Key ?? RuntimeInstanceId.ToString("N");
}

public sealed record RunnerCapacityClaim(
    RunnerCapacityAvailability Availability,
    string? RunnerId = null,
    RunnerCapacityClaimState? State = null,
    RunnerAdmissionFailure? Failure = null);

public interface IRunnerCapacityGate
{
    Task RecordWaitingAsync(Guid runtimeInstanceId, RunnerAdmissionFailure? failure, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<IReadOnlyDictionary<Guid, RunnerAdmissionFailure>> ReadWaitingAsync(IReadOnlyList<Guid> runtimeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, RunnerAdmissionFailure>>(new Dictionary<Guid, RunnerAdmissionFailure>());
    Task CompleteStartupAsync(Guid runtimeInstanceId, string runnerId, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<bool> CanCreateAsync(Guid runtimeInstanceId, string runnerId, CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> CanCreateWorkloadAsync(RuntimeWorkloadIdentity identity, Guid factId, string runnerId,
        CancellationToken cancellationToken) => Task.FromResult(false);
    Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
        string runnerPool,
        string runnerId,
        CancellationToken cancellationToken);

    Task<RunnerPoolInventory> GetPoolInventoryAsync(
        string runnerPool,
        CancellationToken cancellationToken);

    Task<RunnerCapacityClaim> TryClaimAsync(
        RunnerCapacityRequest request,
        CancellationToken cancellationToken);

    Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
        RunnerCapacityRequest request,
        string runnerId,
        CancellationToken cancellationToken);

    Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
        Guid runtimeInstanceId,
        string runnerId,
        CancellationToken cancellationToken);

    Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken cancellationToken) =>
        identity.IsAuxiliary
            ? throw new NotSupportedException("This capacity gate does not support auxiliary allocations.")
            : ReleaseAsync(identity.RuntimeInstanceId, runnerId, cancellationToken);
}
