namespace NoCTF.Application.Runtime.Ports;

public enum RunnerCapacityAvailability
{
    Claimed,
    Insufficient,
    Unavailable
}

public sealed record RunnerCapacityRequest(
    Guid RuntimeInstanceId,
    string Pool,
    long MemoryBytes,
    long NanoCpus,
    long PidsLimit);

public sealed record RunnerCapacityClaim(
    RunnerCapacityAvailability Availability,
    string? RunnerId = null);

public interface IRunnerCapacityGate
{
    Task<RunnerCapacityClaim> TryClaimAsync(
        RunnerCapacityRequest request,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid runtimeInstanceId,
        string runnerId,
        CancellationToken cancellationToken);
}
