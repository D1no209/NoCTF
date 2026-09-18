using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public sealed record RunnerCapacitySnapshot(
    string RunnerId, string? ResourceDomain, bool Alive,
    RunnerAdmissionState State, RunnerAdmissionFailure? Failure,
    RuntimeResourceAmount? Allocatable, RuntimeResourceAmount? Available,
    RuntimeResourceAmount? Budget, RuntimeResourceAmount? Limits,
    RunnerResourceObservation? Observation, int? StartingPrimary, int? ActiveAuxiliary);

public sealed record RunnerCapacityReport(bool Available, IReadOnlyList<RunnerCapacitySnapshot> Runners, bool Truncated = false);

public interface IRunnerCapacityDiagnostics
{
    Task<RunnerCapacityReport> ReadAsync(CancellationToken ct);
}

public sealed class ObserveRunnerCapacity(IRunnerCapacityDiagnostics diagnostics)
{
    public Task<RunnerCapacityReport> ExecuteAsync(CancellationToken ct) => diagnostics.ReadAsync(ct);
}
