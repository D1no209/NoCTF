using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Messages;

public sealed class RunnerCapacityUnavailableException(RunnerAdmissionFailure failure)
    : InvalidOperationException("The Runner cannot currently admit the checker workload.")
{
    public RunnerAdmissionFailure Failure { get; } = failure;
}

public sealed class AuxiliaryRuntimeCapacity(
    IRunnerCapacityGate capacity,
    IOptions<RunnerOptions> runnerOptions,
    IEnumerable<IRuntimeManagedResourceReconciler> reconcilers,
    RunnerResourceMutationCoordinator mutations,
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events,
    TimeProvider clock,
    ILogger<AuxiliaryRuntimeCapacity> logger,
    RuntimeExecutionOptions? executionOptions = null)
{
    public async Task<OneShotResult> RunAsync(ContainerRequest request, RuntimeWorkloadIdentity identity,
        Guid factId, Func<ContainerRequest, CancellationToken, Task<OneShotResult>> execute, CancellationToken ct)
    {
        using var mutation = await mutations.EnterWorkloadAsync(identity, ct);
        var limits = request.Limits;
        var claim = await capacity.TryClaimForRunnerAsync(new(identity.RuntimeInstanceId, runnerOptions.Value.Pool,
            limits.MemoryBytes, limits.CpuMillicores, limits.PidsLimit, identity, factId, ProcessesPerService: executionOptions?.ProcessesPerService ?? 256), runnerOptions.Value.Id, ct);
        if (claim.Availability != RunnerCapacityAvailability.Claimed)
            throw new RunnerCapacityUnavailableException(claim.Failure ?? RunnerAdmissionFailure.NoEligibleRunner);
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        labels["noctf.io/operation-id"] = identity.OperationId.ToString("N");
        labels["noctf.io/workload-kind"] = ((short)identity.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (!await capacity.CanCreateWorkloadAsync(identity, factId, runnerOptions.Value.Id, ct))
                throw new RunnerCapacityUnavailableException(RunnerAdmissionFailure.NoEligibleRunner);
            return await execute(request with { Labels = labels }, ct);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var reconciler = reconcilers.Single(candidate => candidate.Provider == request.Provider);
                if (await reconciler.WorkloadExistsAsync(identity, cleanup.Token) == false)
                    await capacity.ReleaseWorkloadAsync(identity, runnerOptions.Value.Id, cleanup.Token);
            }
            catch (Exception exception)
            {
                // Keep the committed allocation; the resource reconciler retries cleanup.
                logger.LogWarning("Checker capacity retained for {OperationId} after {FailureType}.",
                    identity.OperationId, exception.GetType().Name);
            }
        }
    }

    public async Task RecordAwdAdmissionFailureAsync(Guid factId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(item => item.Id == factId, ct);
        if (fact is null || fact.Kind != GameplayFactKind.AwdServiceTransition
            || fact.State != GameplayFactState.Processing)
            return;
        fact.State = GameplayFactState.PlatformFailed;
        fact.FailureCode = GameplayFactFailureCode.CheckerPlatformError;
        fact.UpdatedAt = clock.GetUtcNow();
        await events.RecordAsync(new(fact.CompetitionId, CompetitionEventKind.GameplayFactAdjudicated,
            CompetitionEventLevel.Error, CompetitionEventVisibility.Staff, fact.UpdatedAt,
            TeamId: fact.TeamId, CompetitionChallengeId: fact.CompetitionChallengeId,
            GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State,
            GameplayFactResult: fact.Result), ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
    }
}
