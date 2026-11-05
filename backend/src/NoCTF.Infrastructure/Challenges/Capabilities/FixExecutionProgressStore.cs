using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Capabilities;

public sealed class FixExecutionProgressStore(IDbContextFactory<NoCtfDbContext> contexts) : IFixExecutionProgressStore
{
    public async Task<bool> RecordMutationStartedAsync(Guid executionId, Guid runtimeId, FixTargetKind targetKind, DateTimeOffset now, CancellationToken ct,
        CapabilityExecutionScope scope = CapabilityExecutionScope.GameplayFact)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (scope == CapabilityExecutionScope.TemplateTest)
        {
            var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(value => value.Id == runtimeId, ct);
            if (runtime is not { Purpose: RuntimePurpose.TemplateTest, State: RuntimeState.Running, CapabilityTest: { } test }
                || test.ExecutionId != executionId || test.Kind == NoCTF.Domain.Challenges.Testing.TemplateCapabilityTestKind.Check
                || test.State != NoCTF.Domain.Challenges.Testing.TemplateCapabilityTestState.Applying
                || test.MutationStartedAt is not null || now >= test.Deadline || targetKind != FixTargetKind.Isolated) return false;
            test.MutationStartedAt = now;
            test.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return true;
        }
        if (scope != CapabilityExecutionScope.GameplayFact) throw new ArgumentOutOfRangeException(nameof(scope));
        var fact = await db.GameplayFacts.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == executionId, ct);
        if (fact is not { Kind: GameplayFactKind.FixAttempt, State: GameplayFactState.Processing, FixExecution: { } execution }
            || execution.RuntimeInstanceId != runtimeId || execution.TargetKind != targetKind || now >= execution.Deadline
            || execution.MutationStartedAt is not null || execution.Phase is not (FixExecutionPhase.Queued or FixExecutionPhase.Applying)) return false;
        if (!await db.RuntimeInstances.AnyAsync(runtime => runtime.Id == runtimeId && runtime.State == RuntimeState.Running
            && runtime.CompetitionId == fact.CompetitionId && runtime.CompetitionChallengeId == fact.CompetitionChallengeId
            && runtime.TeamId == fact.TeamId, ct)) return false;
        execution.Phase = FixExecutionPhase.Applying; execution.MutationStartedAt = now; execution.MutationBlocked = true;
        execution.StartedAt ??= now; fact.UpdatedAt = now;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RecordAppliedAsync(FixOutcome outcome, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == outcome.GameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.FixAttempt, State: GameplayFactState.Processing, FixExecution: { Phase: FixExecutionPhase.Applying } execution }
            || execution.RuntimeInstanceId != outcome.RuntimeInstanceId || fact.CapabilityOutcomes.Any(value => value.Kind == ChallengeCapabilityKind.Fix)) return false;
        if (!await db.RuntimeInstances.AnyAsync(runtime => runtime.Id == execution.RuntimeInstanceId && runtime.State == RuntimeState.Running
                && runtime.GameplayFactId == fact.Id, ct)) return false;
        fact.CapabilityOutcomes.Add(outcome);
        execution.AppliedAt = outcome.ObservedAt;
        execution.MutationBlocked = outcome.ExecutionUncertain;
        execution.Phase = outcome is { ExecutionState: CapabilityExecutionState.Completed, Result: FixResult.Applied }
            ? FixExecutionPhase.Checking : FixExecutionPhase.Finalizing;
        fact.UpdatedAt = outcome.ObservedAt;
        fact.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
