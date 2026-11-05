using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.GameplayFacts.Awdp;

public enum AwdpFixRuntimeCleanupMode
{
    EnsureStop,
    CallerManaged
}

public sealed record AwdpFixFailureConvergenceResult(
    bool FactConverged,
    bool RuntimeStateChanged,
    bool CleanupRequested);

public static class AwdpFixFailureConvergence
{
    public static async Task<AwdpFixFailureConvergenceResult> ConvergeAwdpFixFailureAsync(
        RuntimeInstance runtime,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        ICompetitionEventRecorder events,
        DateTimeOffset failedAt,
        AwdpFixRuntimeCleanupMode cleanupMode,
        CancellationToken cancellationToken)
    {
        if (runtime.Purpose is not (RuntimePurpose.FixTarget
                or RuntimePurpose.ScopedFixTarget)
            || runtime.GameplayFactId is not Guid gameplayFactId)
            return new(false, false, false);

        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            candidate => candidate.Id == gameplayFactId,
            cancellationToken);
        if (fact is null
            || fact.Kind != GameplayFactKind.FixAttempt
            || fact.Id != runtime.GameplayFactId
            || fact.CompetitionId != runtime.CompetitionId
            || fact.CompetitionChallengeId != runtime.CompetitionChallengeId
            || fact.TeamId != runtime.TeamId)
            return new(false, false, false);

        if (runtime.State == RuntimeState.Stopped && fact.State == GameplayFactState.Processing
            && fact.Result is not null && fact.FixExecution is { Phase: FixExecutionPhase.Finalizing, WorkCompletedAt: not null, ExecutionFailure: null })
        {
            await outbox.PublishAsync(new FinalizeFixExecution(fact.Id, runtime.Id));
            return new(false, false, false);
        }

        var factConverged = fact.State is GameplayFactState.Pending
            or GameplayFactState.Queued or GameplayFactState.Processing;
        if (factConverged)
        {
            if (fact.FixExecution is { } execution)
            {
                execution.FailedPhase ??= execution.Phase;
                execution.ExecutionFailure ??= runtime.FailureCode == RuntimeFailureCode.CleanupFailed
                    ? CapabilityExecutionFailure.ResourceCleanupFailed : CapabilityExecutionFailure.ExecutionInterrupted;
                if (execution.Phase == FixExecutionPhase.Checking && !fact.CapabilityOutcomes.Any(outcome => outcome.Kind == ChallengeCapabilityKind.Check))
                    fact.CapabilityOutcomes.Add(new CheckOutcome
                    {
                        GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id, ObservedAt = failedAt,
                        ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = execution.ExecutionFailure
                    });
                if (execution.Phase is FixExecutionPhase.Queued or FixExecutionPhase.Applying
                    && !fact.CapabilityOutcomes.Any(outcome => outcome.Kind == ChallengeCapabilityKind.Fix))
                    fact.CapabilityOutcomes.Add(new FixOutcome
                    {
                        GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id, ObservedAt = failedAt,
                        ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = execution.ExecutionFailure
                    });
            }
            fact.State = GameplayFactState.PlatformFailed;
            fact.Result = null;
            fact.FailureCode = runtime.Purpose == RuntimePurpose.FixTarget
                ? GameplayFactFailureCode.AwdpPlatformFailed
                : GameplayFactFailureCode.FixPlatformFailure;
            fact.UpdatedAt = failedAt;
            await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
            var roundId = await db.LiveSoloSubmissions.Where(submission => submission.GameplayFactId == fact.Id)
                .Select(submission => (Guid?)submission.RoundId).SingleOrDefaultAsync(cancellationToken);
            if (roundId is Guid scopedRound) await outbox.PublishAsync(new NoCTF.Application.LiveSolo.Rounds.ResolveLiveSoloSubmissionResults(scopedRound));
            await RecordFactEventsAsync(
                fact,
                runtime,
                events,
                failedAt,
                cancellationToken);
        }

        if (fact.FixExecution is { } details)
        {
            details.Phase = runtime.State == RuntimeState.Stopped ? FixExecutionPhase.Finished : FixExecutionPhase.Finalizing;
            if (runtime.State == RuntimeState.Stopped) details.FinalizedAt = runtime.StoppedAt ?? failedAt;
        }

        var runtimeStateChanged = false;
        var cleanupRequested = false;
        if (cleanupMode == AwdpFixRuntimeCleanupMode.EnsureStop
            && runtime.State is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping)
        {
            if (runtime.State != RuntimeState.Stopping)
            {
                runtime.State = RuntimeState.Stopping;
                runtimeStateChanged = true;
                await events.RecordAsync(new(
                    runtime.CompetitionId!.Value,
                    CompetitionEventKind.RuntimeStateChanged,
                    CompetitionEventLevel.Warning,
                    CompetitionEventVisibility.Team,
                    failedAt,
                    TeamId: runtime.TeamId,
                    CompetitionChallengeId: runtime.CompetitionChallengeId,
                    RuntimeInstanceId: runtime.Id,
                    GameplayFactId: fact.Id,
                    RuntimeState: runtime.State), cancellationToken);
            }

            await outbox.PublishAsync(new StopRuntime(runtime.Id));
            cleanupRequested = true;
        }

        return new(factConverged, runtimeStateChanged, cleanupRequested);
    }

    private static async Task RecordFactEventsAsync(
        GameplayFact fact,
        RuntimeInstance runtime,
        ICompetitionEventRecorder events,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        await events.RecordAsync(new(
            fact.CompetitionId!.Value,
            CompetitionEventKind.GameplayFactAdjudicated,
            CompetitionEventLevel.Error,
            CompetitionEventVisibility.Team,
            failedAt,
            ActorUserId: fact.ActorUserId,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            RuntimeState: runtime.State), cancellationToken);

        if (runtime.Purpose != RuntimePurpose.FixTarget)
            return;

        if (fact.ReferenceId is not Guid patchUploadId
            || fact.TeamId is not Guid teamId)
            return;
        await events.RecordAsync(new(
            fact.CompetitionId!.Value,
            CompetitionEventKind.AwdpFixResolved,
            CompetitionEventLevel.Error,
            CompetitionEventVisibility.Public,
            failedAt,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            PatchUploadId: patchUploadId,
            AwdpFixOutcome: AwdpFixOutcome.PlatformFailed,
            GameplayFactFailureCode: fact.FailureCode,
            ResolvedAt: failedAt), cancellationToken);
    }

}
