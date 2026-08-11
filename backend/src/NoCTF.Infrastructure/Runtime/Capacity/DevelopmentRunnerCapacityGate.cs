using NoCTF.Application.Runtime.Capacity;
using Microsoft.Extensions.Configuration;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class DevelopmentRunnerCapacityGate(IConfiguration configuration)
    : IRunnerCapacityGate
{
    private readonly string runnerId = configuration["Runner:Id"] ?? "local-development";
    private readonly string runnerPool = configuration["Runner:Pool"] ?? "development";
    private readonly Dictionary<Guid, string> claims = [];
    private readonly Lock sync = new();

    public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
        string runnerPool,
        string runnerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            string.Equals(runnerPool, this.runnerPool, StringComparison.Ordinal)
            && string.Equals(runnerId, this.runnerId, StringComparison.Ordinal)
                ? RunnerHeartbeatStatus.Online
                : RunnerHeartbeatStatus.Offline);

    public Task<RunnerPoolInventory> GetPoolInventoryAsync(
        string runnerPool,
        CancellationToken cancellationToken) =>
        Task.FromResult(new RunnerPoolInventory(
            RunnerPoolInventoryAvailability.Available,
            string.Equals(runnerPool, this.runnerPool, StringComparison.Ordinal)
                ? [runnerId]
                : []));

    public Task<RunnerCapacityClaim> TryClaimAsync(
        RunnerCapacityRequest request,
        CancellationToken cancellationToken) =>
        TryClaimCore(request, runnerId, cancellationToken);

    public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
        RunnerCapacityRequest request,
        string runnerId,
        CancellationToken cancellationToken) =>
        TryClaimCore(request, runnerId, cancellationToken);

    public Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
        Guid runtimeInstanceId,
        string runnerId,
        CancellationToken cancellationToken) =>
        ReleaseCore(runtimeInstanceId, runnerId, cancellationToken);

    private Task<RunnerCapacityClaim> TryClaimCore(
        RunnerCapacityRequest request,
        string candidateRunnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.Pool, runnerPool, StringComparison.Ordinal)
            || !string.Equals(candidateRunnerId, runnerId, StringComparison.Ordinal))
            return Task.FromResult(new RunnerCapacityClaim(
                RunnerCapacityAvailability.Insufficient));

        lock (sync)
        {
            if (claims.TryGetValue(request.RuntimeInstanceId, out var owner))
            {
                return Task.FromResult(new RunnerCapacityClaim(
                    RunnerCapacityAvailability.Claimed,
                    owner,
                    RunnerCapacityClaimState.AlreadyOwned));
            }
            claims.Add(request.RuntimeInstanceId, runnerId);
        }
        return Task.FromResult(new RunnerCapacityClaim(
            RunnerCapacityAvailability.Claimed,
            runnerId,
            RunnerCapacityClaimState.Acquired));
    }

    private Task<RunnerCapacityReleaseOutcome> ReleaseCore(
        Guid runtimeInstanceId,
        string candidateRunnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!claims.TryGetValue(runtimeInstanceId, out var owner))
                return Task.FromResult(RunnerCapacityReleaseOutcome.AlreadyReleased);
            if (!string.Equals(owner, candidateRunnerId, StringComparison.Ordinal))
                return Task.FromResult(RunnerCapacityReleaseOutcome.OwnerMismatch);
            claims.Remove(runtimeInstanceId);
            return Task.FromResult(RunnerCapacityReleaseOutcome.Released);
        }
    }
}
