using NoCTF.Application.Runtime.Capacity;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class DevelopmentRunnerCapacityOptions
{
    public string Id { get; init; } = "local-development";

    public string Pool { get; init; } = "development";
}

public sealed class DevelopmentRunnerCapacityGate(
    IOptions<DevelopmentRunnerCapacityOptions> options)
    : IRunnerCapacityGate
{
    private readonly string runnerId = options.Value.Id;
    private readonly string runnerPool = options.Value.Pool;
    private readonly Dictionary<string, string> claims = [];
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
        ReleaseCore(runtimeInstanceId.ToString("N"), runnerId, cancellationToken);

    public Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadAsync(RuntimeWorkloadIdentity identity, string runnerId,
        CancellationToken cancellationToken) => ReleaseCore(identity.Key, runnerId, cancellationToken);

    public Task<bool> CanCreateWorkloadAsync(RuntimeWorkloadIdentity identity, Guid factId, string runnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) return Task.FromResult(claims.TryGetValue(identity.Key, out var owner) && owner == runnerId);
    }

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
            if (claims.TryGetValue(request.ClaimSuffix, out var owner))
            {
                return Task.FromResult(new RunnerCapacityClaim(
                    RunnerCapacityAvailability.Claimed,
                    owner,
                    RunnerCapacityClaimState.AlreadyOwned));
            }
            claims.Add(request.ClaimSuffix, runnerId);
        }
        return Task.FromResult(new RunnerCapacityClaim(
            RunnerCapacityAvailability.Claimed,
            runnerId,
            RunnerCapacityClaimState.Acquired));
    }

    private Task<RunnerCapacityReleaseOutcome> ReleaseCore(
        string claimKey,
        string candidateRunnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!claims.TryGetValue(claimKey, out var owner))
                return Task.FromResult(RunnerCapacityReleaseOutcome.AlreadyReleased);
            if (!string.Equals(owner, candidateRunnerId, StringComparison.Ordinal))
                return Task.FromResult(RunnerCapacityReleaseOutcome.OwnerMismatch);
            claims.Remove(claimKey);
            return Task.FromResult(RunnerCapacityReleaseOutcome.Released);
        }
    }
}
