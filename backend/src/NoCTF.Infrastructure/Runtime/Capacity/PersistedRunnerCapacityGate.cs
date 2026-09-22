using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Capacity;

/// <summary>Commit an allocation before publishing any provider command; Redis remains a recoverable index.</summary>
public sealed class PersistedRunnerCapacityGate(
    NoCtfDbContext db,
    RedisRunnerCapacityGate redis,
    ITransactionalMessageOutbox outbox) : IRunnerCapacityGate
{
    public Task RecordWaitingAsync(Guid runtimeId, RunnerAdmissionFailure? failure, CancellationToken ct) => redis.RecordWaitingAsync(runtimeId, failure, ct);
    public Task<IReadOnlyDictionary<Guid, RunnerAdmissionFailure>> ReadWaitingAsync(IReadOnlyList<Guid> runtimeIds, CancellationToken ct) =>
        redis.ReadWaitingAsync(runtimeIds, ct);
    public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(string runnerPool, string runnerId, CancellationToken ct) =>
        redis.GetHeartbeatAsync(runnerPool, runnerId, ct);

    public Task<RunnerPoolInventory> GetPoolInventoryAsync(string runnerPool, CancellationToken ct) =>
        redis.GetPoolInventoryAsync(runnerPool, ct);

    public async Task CompleteStartupAsync(Guid runtimeInstanceId, string runnerId, CancellationToken ct)
    {
        var document = await db.RuntimeInstances.AsNoTracking().Where(runtime => runtime.Id == runtimeInstanceId)
            .Select(runtime => runtime.CapacityAllocations).SingleOrDefaultAsync(ct);
        var primary = document?.Items.SingleOrDefault(item => !item.Identity.IsAuxiliary && item.RunnerId == runnerId);
        if (primary is not null) await redis.CompleteWorkloadStartupAsync(primary.Identity, runnerId, ct);
    }

    public async Task<bool> CanCreateAsync(Guid runtimeInstanceId, string runnerId, CancellationToken ct)
    {
        var document = await db.RuntimeInstances.AsNoTracking().Where(runtime => runtime.Id == runtimeInstanceId
                && runtime.State == RuntimeState.Provisioning && runtime.RunnerId == runnerId)
            .Select(runtime => runtime.CapacityAllocations).SingleOrDefaultAsync(ct);
        var primary = document?.Items.SingleOrDefault(item => !item.Identity.IsAuxiliary && item.RunnerId == runnerId);
        return primary is not null && await redis.ValidateClaimAsync(primary, ct);
    }

    public Task<RunnerCapacityClaim> TryClaimAsync(RunnerCapacityRequest request, CancellationToken ct) =>
        ClaimAsync(request, null, ct);

    public async Task<bool> CanCreateWorkloadAsync(RuntimeWorkloadIdentity identity, Guid factId, string runnerId, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(row => row.Id == identity.RuntimeInstanceId
            && row.RunnerId == runnerId && row.State == RuntimeState.Running, ct);
        var allocation = runtime?.CapacityAllocations.Items.SingleOrDefault(item => item.Identity == identity
            && item.GameplayFactId == factId && item.RunnerId == runnerId);
        return allocation is not null
            && await db.GameplayFacts.AnyAsync(fact => fact.Id == factId && fact.State == GameplayFactState.Processing, ct)
            && await redis.ValidateClaimAsync(allocation, ct);
    }

    public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(RunnerCapacityRequest request, string runnerId, CancellationToken ct) =>
        ClaimAsync(request, runnerId, ct);

    private async Task<RunnerCapacityClaim> ClaimAsync(RunnerCapacityRequest request, string? runnerId, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await RuntimeCapacityCriticalSection.AcquireAsync(db, ct);
        var runtime = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RuntimeInstanceId, ct);
        if (runtime is null || runtime.State is RuntimeState.Stopped or RuntimeState.Stopping or RuntimeState.Failed)
            return new(RunnerCapacityAvailability.Unavailable, Failure: RunnerAdmissionFailure.NoEligibleRunner);
        var identity = request.Workload ?? PrimaryIdentity(runtime);
        identity.Validate();
        if (identity.RuntimeInstanceId != runtime.Id)
            throw new InvalidOperationException("The capacity identity belongs to another Runtime.");
        if (!identity.IsAuxiliary && identity != PrimaryIdentity(runtime)
            || runtime.RunnerId is { } owner && runnerId is not null && owner != runnerId)
            return new(RunnerCapacityAvailability.Unavailable, Failure: RunnerAdmissionFailure.NoEligibleRunner);
        runnerId ??= runtime.RunnerId;
        if (identity.IsAuxiliary && (request.GameplayFactId is not Guid factId
            || !await db.GameplayFacts.AnyAsync(fact => fact.Id == factId
                && fact.State == GameplayFactState.Processing
                && fact.CompetitionChallengeId == runtime.CompetitionChallengeId && fact.TeamId == runtime.TeamId
                && (identity.Kind == RuntimeWorkloadKind.AwdChecker && fact.Kind == GameplayFactKind.AwdServiceTransition
                    || identity.Kind == RuntimeWorkloadKind.PatchChecker && runtime.GameplayFactId == fact.Id), ct)))
            return new(RunnerCapacityAvailability.Unavailable, Failure: RunnerAdmissionFailure.NoEligibleRunner);
        var existing = runtime.CapacityAllocations.Items.SingleOrDefault(item => item.Identity == identity);
        var limit = existing?.Limit ?? request.Limit ?? new(request.MemoryBytes, request.NanoCpus, request.PidsLimit);
        var budget = existing?.Budget ?? new RuntimeResourceAmount(request.MemoryBytes, request.NanoCpus, request.PidsLimit);
        request = request with
        {
            Workload = identity, MemoryBytes = budget.MemoryBytes, NanoCpus = budget.NanoCpus, PidsLimit = budget.PidsLimit
        };
        runnerId ??= existing?.RunnerId;
        var claim = runnerId is null
            ? await redis.TryClaimAsync(request, ct)
            : await redis.TryClaimForRunnerAsync(request, runnerId, ct);
        if (claim.Availability != RunnerCapacityAvailability.Claimed || claim.RunnerId is null)
            return claim;
        var allocation = existing ?? new RuntimeCapacityAllocation(identity, request.GameplayFactId,
            await redis.GetResourceDomainAsync(claim.RunnerId, ct), claim.RunnerId, budget, limit);
        if (allocation.RunnerId != claim.RunnerId)
            throw new InvalidOperationException("A capacity allocation cannot change Runner ownership.");
        var document = runtime.CapacityAllocations.Add(allocation);
        await db.RuntimeInstances.Where(x => x.Id == runtime.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CapacityAllocations, document), ct);
        if (transaction is not null)
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return claim;
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseAsync(Guid runtimeId, string runnerId, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runtimeId, ct);
        if (runtime is null)
            return RunnerCapacityReleaseOutcome.RecoveryRequired;
        var primary = runtime.CapacityAllocations.Items.SingleOrDefault(item => !item.Identity.IsAuxiliary);
        return primary is null
            ? RunnerCapacityReleaseOutcome.RecoveryRequired
            : await ReleaseWorkloadAsync(primary.Identity, runnerId, ct);
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await RuntimeCapacityCriticalSection.AcquireAsync(db, ct);
        var document = await db.RuntimeInstances.AsNoTracking().Where(x => x.Id == identity.RuntimeInstanceId)
            .Select(x => x.CapacityAllocations).SingleOrDefaultAsync(ct);
        var allocation = document?.Items.SingleOrDefault(item => item.Identity == identity);
        if (allocation is null)
            return RunnerCapacityReleaseOutcome.AlreadyReleased;
        if (allocation.RunnerId != runnerId)
            return RunnerCapacityReleaseOutcome.OwnerMismatch;
        var next = document!.Remove(identity);
        await db.RuntimeInstances.Where(x => x.Id == identity.RuntimeInstanceId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CapacityAllocations, next), ct);
        await outbox.PublishAsync(new ReleaseRunnerCapacity(identity, runnerId));
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
        }
        return RunnerCapacityReleaseOutcome.Released;
    }

    public static RuntimeWorkloadIdentity PrimaryIdentity(RuntimeInstance runtime) => new(
        runtime.Purpose is RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget
            ? RuntimeWorkloadKind.VerificationTarget
            : runtime.RuntimeKind == RuntimeKind.Compose ? RuntimeWorkloadKind.Compose : RuntimeWorkloadKind.Runtime,
        runtime.Id, runtime.Id);
}
