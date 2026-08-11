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
    OwnerMismatch
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
    long PidsLimit);

public sealed record RunnerCapacityClaim(
    RunnerCapacityAvailability Availability,
    string? RunnerId = null,
    RunnerCapacityClaimState? State = null);

public interface IRunnerCapacityGate
{
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

    /// <summary>
    /// Releases a capacity claim whose owner may predate the current Runner assignment.
    /// This is only for fail-closed orphan recovery keyed by RuntimeInstanceId.
    /// </summary>
    Task<RunnerCapacityReleaseOutcome> ReleaseOrphanedAsync(
        Guid runtimeInstanceId,
        CancellationToken cancellationToken) =>
        Task.FromResult(RunnerCapacityReleaseOutcome.AlreadyReleased);
}
