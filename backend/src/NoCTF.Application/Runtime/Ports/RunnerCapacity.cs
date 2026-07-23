namespace NoCTF.Application.Runtime.Ports;

public enum RunnerCapacityAvailability
{
    Claimed,
    Insufficient,
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
}
