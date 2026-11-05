using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd.Fixes;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Capacity;

namespace NoCTF.Infrastructure.Competitions.Awd.Fixes;

public sealed class AwdPersistentFixStore(NoCtfDbContext db, IPostCommitMessagePublisher messages,
    ICompetitionEventRecorder events) : IAwdPersistentFixStore
{
    public async Task<PersistentFixWork?> ClaimAsync(RunPersistentFix request, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == request.GameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.FixAttempt, State: GameplayFactState.Pending,
                FixExecution: { TargetKind: FixTargetKind.Persistent, Phase: FixExecutionPhase.Queued } execution }
            || execution.RuntimeInstanceId != request.RuntimeInstanceId || execution.Deadline != request.Deadline || now >= execution.Deadline)
            return null;
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == request.RuntimeInstanceId
            && value.CompetitionId == fact.CompetitionId && value.CompetitionChallengeId == fact.CompetitionChallengeId
            && value.TeamId == fact.TeamId && value.State == RuntimeState.Running && value.RunnerId == request.RunnerId, ct);
        if (runtime is not { Purpose: RuntimePurpose.Player, ProviderReceipt: ContainerRuntimeReceipt }) return null;
        var allowed = await db.Set<AwdCompetitionChallengeRules>().AnyAsync(rule => rule.CompetitionChallengeId == fact.CompetitionChallengeId
            && db.CompetitionChallenges.Any(challenge => challenge.Id == rule.CompetitionChallengeId
                && challenge.CompetitionId == fact.CompetitionId && challenge.IsPublished
                && db.Competitions.Any(competition => competition.Id == challenge.CompetitionId && competition.Mode == GameMode.Awd
                    && (competition.Status == CompetitionStatus.Running || competition.Status == CompetitionStatus.Paused || competition.Status == CompetitionStatus.Finished))), ct);
        if (!allowed) return null;
        var definition = await db.Set<FixCapabilityDefinition>().AsNoTracking().SingleAsync(capability =>
            db.CompetitionChallenges.Any(challenge => challenge.Id == fact.CompetitionChallengeId && challenge.ChallengeId == capability.ChallengeId), ct);
        var upload = await db.PatchUploads.AsNoTracking().Include(value => value.File).SingleAsync(value => value.Id == fact.ReferenceId, ct);
        execution.Phase = FixExecutionPhase.Queued; execution.MutationBlocked = false; execution.StartedAt = now;
        fact.State = GameplayFactState.Processing; fact.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
        await messages.FlushCommittedMessagesAsync();
        return new(fact.Id, runtime.Id, (ContainerRuntimeReceiptData)runtime.ProviderReceipt.ToData(), definition,
            new(upload.Id, upload.File.FileName, upload.File.ByteLength, upload.File.Sha256), execution.Deadline);
    }

    public async Task<bool> CompleteAsync(FixOutcome outcome, DateTimeOffset now, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Persistent Fix completion requires its own transaction.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == outcome.GameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.FixAttempt, FixExecution: { TargetKind: FixTargetKind.Persistent } execution }
            || execution.RuntimeInstanceId != outcome.RuntimeInstanceId || execution.Phase == FixExecutionPhase.Finished
            || fact.State is not (GameplayFactState.Pending or GameplayFactState.Processing or GameplayFactState.PlatformFailed)) return false;
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == execution.RuntimeInstanceId, ct);
        if (runtime.CompetitionId != fact.CompetitionId || runtime.CompetitionChallengeId != fact.CompetitionChallengeId || runtime.TeamId != fact.TeamId) return false;
        outcome.ExecutionUncertain = outcome.ExecutionUncertain && execution.MutationStartedAt is not null;
        var current = fact.CapabilityOutcomes.OfType<FixOutcome>().SingleOrDefault();
        if (current is null) fact.CapabilityOutcomes.Add(outcome);
        else
        {
            current.Result = outcome.Result; current.ExitCode = outcome.ExitCode; current.ExecutionState = outcome.ExecutionState;
            current.ExecutionFailure = outcome.ExecutionFailure; current.ObservedAt = outcome.ObservedAt;
            current.ExecutionUncertain = outcome.ExecutionUncertain; current.ExecutionIdentity = outcome.ExecutionIdentity;
        }
        execution.AppliedAt = outcome.ExecutionUncertain ? null : outcome.ObservedAt;
        execution.MutationCompletedAt = outcome.ExecutionUncertain ? null : outcome.ObservedAt;
        if (outcome is { ExecutionUncertain: true, ExecutionState: CapabilityExecutionState.Completed, Result: FixResult.TimedOut, ExecutionIdentity: not null })
            execution.LastConfirmedRunningAt = outcome.ObservedAt;
        execution.MutationBlocked = outcome.ExecutionUncertain && runtime.State != RuntimeState.Stopped;
        execution.Phase = execution.MutationBlocked ? FixExecutionPhase.Finalizing : FixExecutionPhase.Finished;
        if (!execution.MutationBlocked) execution.FinalizedAt = now;
        if (outcome.ExecutionState == CapabilityExecutionState.PlatformFailed || outcome.ExecutionUncertain)
            execution.FailedPhase ??= execution.MutationStartedAt is null ? FixExecutionPhase.Queued : FixExecutionPhase.Applying;
        if (outcome.ExecutionState == CapabilityExecutionState.PlatformFailed)
        {
            fact.State = GameplayFactState.PlatformFailed; fact.Result = null;
            fact.FailureCode = outcome.ExecutionUncertain ? GameplayFactFailureCode.FixExecutionUncertain : GameplayFactFailureCode.FixPlatformFailure;
        }
        else
        {
            fact.State = GameplayFactState.Completed;
            fact.Result = outcome.Result == FixResult.Applied && outcome.ObservedAt <= execution.Deadline ? GameplayFactResult.Applied : GameplayFactResult.Rejected;
            fact.FailureCode = fact.Result == GameplayFactResult.Applied ? null
                : outcome.Result == FixResult.TimedOut || outcome.ObservedAt > execution.Deadline ? GameplayFactFailureCode.FixExecutionTimedOut : GameplayFactFailureCode.FixExecutionFailed;
        }
        fact.UpdatedAt = now;
        await events.RecordAsync(new(fact.CompetitionId!.Value, CompetitionEventKind.GameplayFactAdjudicated,
            fact.State == GameplayFactState.PlatformFailed ? CompetitionEventLevel.Error : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team, now, TeamId: fact.TeamId, CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id, GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State, GameplayFactResult: fact.Result), ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
        await messages.FlushCommittedMessagesAsync();
        return true;
    }

    public async Task RecoverAsync(DateTimeOffset now, IRunnerCapacityGate capacity, IRuntimePlacementPolicy placement, CancellationToken ct)
    {
        var facts = await db.GameplayFacts.AsSplitQuery().Where(fact => fact.Kind == GameplayFactKind.FixAttempt
                && fact.FixExecution != null && fact.FixExecution.TargetKind == FixTargetKind.Persistent
                && fact.FixExecution.Phase != FixExecutionPhase.Finished
                && (fact.State == GameplayFactState.Pending || fact.State == GameplayFactState.Processing
                    || fact.FixExecution.MutationBlocked && db.RuntimeInstances.Any(runtime => runtime.Id == fact.FixExecution.RuntimeInstanceId
                        && runtime.State == RuntimeState.Stopped)))
            .OrderBy(fact => fact.UpdatedAt).ThenBy(fact => fact.Id).Take(64).ToArrayAsync(ct);
        foreach (var fact in facts)
        {
            var execution = fact.FixExecution!;
            var runtime = await db.RuntimeInstances.AsNoTracking().SingleAsync(value => value.Id == execution.RuntimeInstanceId, ct);
            if (runtime.State == RuntimeState.Stopped)
            {
                if (fact.State is GameplayFactState.Pending or GameplayFactState.Processing) execution.FailedPhase ??= execution.Phase;
                execution.MutationBlocked = false; execution.Phase = FixExecutionPhase.Finished; execution.FinalizedAt = runtime.StoppedAt ?? now;
                if (fact.State is GameplayFactState.Pending or GameplayFactState.Processing)
                { fact.State = GameplayFactState.PlatformFailed; fact.Result = null; fact.FailureCode = GameplayFactFailureCode.FixPlatformFailure; }
                if (!fact.CapabilityOutcomes.Any(outcome => outcome.Kind == ChallengeCapabilityKind.Fix))
                    fact.CapabilityOutcomes.Add(new FixOutcome { GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id, ObservedAt = now,
                        ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = CapabilityExecutionFailure.TargetSuperseded });
                fact.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
            }
            else if (now >= execution.Deadline && fact.State is GameplayFactState.Pending or GameplayFactState.Processing)
            {
                await CompleteAsync(new()
                {
                    GameplayFactId = fact.Id, RuntimeInstanceId = execution.RuntimeInstanceId, ObservedAt = now,
                    ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = CapabilityExecutionFailure.ExecutionInterrupted,
                    ExecutionUncertain = execution.MutationStartedAt is not null
                }, now, ct);
            }
            else if (fact.State == GameplayFactState.Pending && runtime.State == RuntimeState.Running && runtime.RunnerId is { } runner)
                if (await capacity.GetHeartbeatAsync(placement.Resolve(runtime.RuntimeKind).RunnerPool, runner, ct) == RunnerHeartbeatStatus.Online)
                    await messages.PublishToRunnerNodeAsync(new RunPersistentFix(fact.Id, runtime.Id, execution.Deadline, runner));
        }
        var observations = await db.GameplayFacts.AsNoTracking().IgnoreAutoIncludes().Where(fact => fact.Kind == GameplayFactKind.FixAttempt
                && (fact.State == GameplayFactState.PlatformFailed || fact.State == GameplayFactState.Completed) && fact.FixExecution != null && fact.FixExecution.MutationBlocked
                && fact.FixExecution.TargetKind == FixTargetKind.Persistent
                && fact.CapabilityOutcomes.OfType<FixOutcome>().Any(outcome => outcome.ExecutionUncertain && outcome.ExecutionIdentity != null))
            .Join(db.RuntimeInstances.Where(runtime => runtime.State == RuntimeState.Running && runtime.RunnerId != null),
                fact => fact.FixExecution!.RuntimeInstanceId, runtime => runtime.Id, (fact, runtime) => new { FactId = fact.Id, fact.UpdatedAt,
                    RuntimeId = runtime.Id, runtime.RunnerId, runtime.RuntimeKind })
            .OrderBy(value => value.UpdatedAt).ThenBy(value => value.FactId).Take(32).ToArrayAsync(ct);
        foreach (var observation in observations)
            if (await capacity.GetHeartbeatAsync(placement.Resolve(observation.RuntimeKind).RunnerPool, observation.RunnerId!, ct) == RunnerHeartbeatStatus.Online)
                await messages.PublishToRunnerNodeAsync(new ObservePersistentFix(observation.FactId, observation.RuntimeId, observation.RunnerId!, now.ToUnixTimeSeconds() / 10));
        await messages.FlushCommittedMessagesAsync();
    }

    public async Task<PersistentFixObservationWork?> ReadObservationAsync(ObservePersistentFix request, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value => value.Id == request.GameplayFactId
            && (value.State == GameplayFactState.PlatformFailed || value.State == GameplayFactState.Completed) && value.FixExecution != null
            && value.FixExecution.TargetKind == FixTargetKind.Persistent && value.FixExecution.MutationBlocked
            && value.FixExecution.RuntimeInstanceId == request.RuntimeInstanceId, ct);
        var outcome = fact?.CapabilityOutcomes.OfType<FixOutcome>().SingleOrDefault();
        if (fact is null || outcome is not { ExecutionUncertain: true, ExecutionIdentity: { } identity }) return null;
        var runtime = await db.RuntimeInstances.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value => value.Id == request.RuntimeInstanceId
            && value.State == RuntimeState.Running && value.RunnerId == request.RunnerId && value.CompetitionId == fact!.CompetitionId
            && value.CompetitionChallengeId == fact.CompetitionChallengeId && value.TeamId == fact.TeamId, ct);
        if (runtime?.ProviderReceipt is not ContainerRuntimeReceipt receipt) return null;
        var service = await db.Set<FixCapabilityDefinition>().Where(definition => db.CompetitionChallenges.Any(challenge =>
                challenge.Id == fact!.CompetitionChallengeId && challenge.ChallengeId == definition.ChallengeId))
            .Select(definition => definition.TargetServiceName).SingleAsync(ct);
        return new(fact!.Id, runtime.Id, (ContainerRuntimeReceiptData)receipt.ToData(), service, identity);
    }

    public async Task RecordObservationAsync(PersistentFixObservationWork work, ContainerExecObservation observation, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == work.GameplayFactId, ct);
        var outcome = fact.CapabilityOutcomes.OfType<FixOutcome>().SingleOrDefault();
        if (outcome is null || fact.FixExecution is not { MutationBlocked: true, TargetKind: FixTargetKind.Persistent } execution
            || execution.RuntimeInstanceId != work.RuntimeInstanceId || outcome.ExecutionIdentity != work.ExecutionIdentity) return;
        outcome.ObservedAt = now; fact.UpdatedAt = now;
        if (observation.State == ContainerExecState.Running) execution.LastConfirmedRunningAt = now;
        if (observation.State == ContainerExecState.Ended && observation.ExitCode is { } exitCode)
        {
            outcome.ExecutionUncertain = false; outcome.ExitCode = exitCode;
            outcome.Result = exitCode == 0 ? FixResult.Applied : FixResult.Failed;
            outcome.ExecutionState = CapabilityExecutionState.Completed; outcome.ExecutionFailure = null;
            execution.MutationBlocked = false; execution.Phase = FixExecutionPhase.Finished; execution.FinalizedAt = now; execution.AppliedAt = now;
            if (fact.State == GameplayFactState.PlatformFailed) fact.FailureCode = GameplayFactFailureCode.FixPlatformFailure;
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (!execution.MutationBlocked) await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
        await messages.FlushCommittedMessagesAsync();
    }
}
