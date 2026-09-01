using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

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
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        DateTimeOffset failedAt,
        AwdpFixRuntimeCleanupMode cleanupMode,
        CancellationToken cancellationToken)
    {
        if (runtime.Purpose != RuntimePurpose.AwdpTarget
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

        var factConverged = fact.State is GameplayFactState.Pending
            or GameplayFactState.Processing;
        if (factConverged)
        {
            fact.State = GameplayFactState.PlatformFailed;
            fact.Result = null;
            fact.FailureCode = GameplayFactFailureCode.AwdpPlatformFailed;
            fact.UpdatedAt = failedAt;
            await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
            await RecordFactEventsAsync(
                fact,
                runtime,
                events,
                failedAt,
                cancellationToken);
            await QueueNextFixAsync(fact, db, outbox, cancellationToken);
        }

        var runtimeStateChanged = false;
        var cleanupRequested = false;
        if (cleanupMode == AwdpFixRuntimeCleanupMode.EnsureStop
            && runtime.State is RuntimeState.Running or RuntimeState.Stopping)
        {
            if (runtime.State == RuntimeState.Running)
            {
                runtime.State = RuntimeState.Stopping;
                runtimeStateChanged = true;
                await events.RecordAsync(new(
                    runtime.CompetitionId,
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
            fact.CompetitionId,
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

        if (fact.ReferenceId is not Guid patchUploadId
            || fact.TeamId is not Guid teamId)
            return;
        var payload = AwdpFixResolvedEventPayload.Create(
            fact.Id,
            patchUploadId,
            runtime.Id,
            teamId,
            fact.CompetitionChallengeId,
            AwdpFixOutcome.PlatformFailed,
            fact.FailureCode,
            failedAt);
        await events.RecordAsync(new(
            fact.CompetitionId,
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
            PayloadJson: payload.Serialize()), cancellationToken);
    }

    private static async Task QueueNextFixAsync(
        GameplayFact failed,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var nextGameplayFactId = await db.GameplayFacts.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == failed.CompetitionId
                && candidate.CompetitionChallengeId == failed.CompetitionChallengeId
                && candidate.TeamId == failed.TeamId
                && candidate.Kind == GameplayFactKind.FixAttempt
                && candidate.State == GameplayFactState.Queued)
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (nextGameplayFactId is Guid id)
            await outbox.PublishAsync(new EvaluateGameplayFact(id));
    }
}
