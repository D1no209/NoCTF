using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class InternalResultStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder? eventRecorder = null) : IInternalResultStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<InternalResultDisposition> RecordAwdAsync(
        AwdCheckResult result,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null)
            return InternalResultDisposition.NotFound;
        if (runtime.Generation != result.Generation)
            return InternalResultDisposition.Superseded;
        if (runtime.ProcessingVersion < result.ProcessingVersion
            || runtime.CheckerSequence < result.CheckerSequence)
            return InternalResultDisposition.Conflict;
        if (runtime.ProcessingVersion > result.ProcessingVersion
            || runtime.CheckerSequence > result.CheckerSequence)
            return InternalResultDisposition.Superseded;
        var appliedAt = NextAppliedAt(
            result.OccurredAt,
            runtime.CheckerStatusUpdatedAt);

        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == runtime.CompetitionChallengeId)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new
                {
                    Competition = competition
                })
            .SingleAsync(ct);
        if (context.Competition.Mode != GameMode.Awd || runtime.TeamId is null)
            return InternalResultDisposition.NotFound;
        var transitionResult = AwdServiceStateTransition.ToGameplayFactResult(
            runtime.CheckerStatus,
            result.State);
        if (transitionResult is GameplayFactResult scoringResult)
        {
            var fact = new GameplayFact
            {
                Id = Guid.CreateVersion7(appliedAt),
                CompetitionId = runtime.CompetitionId,
                CompetitionChallengeId = runtime.CompetitionChallengeId,
                TeamId = runtime.TeamId,
                Kind = GameplayFactKind.AwdServiceTransition,
                State = GameplayFactState.Completed,
                Result = scoringResult,
                OccurredAt = appliedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.GameplayFacts.Add(fact);
            await events.RecordAsync(new(
                runtime.CompetitionId,
                CompetitionEventKind.ScoringRecorded,
                scoringResult == GameplayFactResult.Wrong
                    ? CompetitionEventLevel.Warning
                    : CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                appliedAt,
                TeamId: runtime.TeamId,
                CompetitionChallengeId: runtime.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind,
                GameplayFactState: fact.State,
                GameplayFactResult: fact.Result,
                RuntimeState: runtime.State,
                RuntimeGeneration: runtime.Generation), ct);
            context.Competition.LeaderboardDirty = true;
        }
        runtime.CheckerStatus = result.State;
        runtime.CheckerStatusUpdatedAt = appliedAt;
        runtime.LastAppliedCheckerSequence = result.CheckerSequence;
        runtime.CheckerDeadlineAt = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return InternalResultDisposition.Applied;
    }

    public async Task<InternalResultDisposition> RecordAwdpAsync(
        AwdpFixResult result,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == result.GameplayFactId, ct);
        if (fact is null)
            return InternalResultDisposition.NotFound;
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null
            || runtime.Purpose != RuntimePurpose.AwdpTarget
            || runtime.GameplayFactId != fact.Id)
            return InternalResultDisposition.NotFound;
        if (runtime.Generation != result.Generation
            || runtime.ProcessingVersion > result.RuntimeProcessingVersion)
            return InternalResultDisposition.Superseded;
        if (runtime.ProcessingVersion < result.RuntimeProcessingVersion)
            return InternalResultDisposition.Conflict;
        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == fact.CompetitionChallengeId)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges,
                scope => scope.Challenge.ChallengeId,
                challenge => challenge.Id,
                (scope, challenge) => new
                {
                    scope.Challenge,
                    scope.Competition,
                    DefinitionRevision = challenge.Revision
                })
            .SingleAsync(ct);
        if (context.Competition.Mode != GameMode.Awdp || fact.Kind != GameplayFactKind.FixAttempt)
            return InternalResultDisposition.NotFound;
        var configurationWasSuperseded =
            runtime.SourceCompetitionConfigurationRevision
                != context.Competition.ConfigurationRevision
            || runtime.SourceCompetitionChallengeRevision != context.Challenge.Revision
            || runtime.SourceChallengeDefinitionRevision != context.DefinitionRevision;
        var decision = AwdpFixOutcomeMapper.Map(result.Outcome);
        if (configurationWasSuperseded
            || decision.Result is null)
        {
            fact.State = GameplayFactState.PlatformFailed;
            fact.FailureCode =
                configurationWasSuperseded
                    ? GameplayFactFailureCode.CheckerPlatformError
                    : decision.FailureCode ?? GameplayFactFailureCode.CheckerPlatformError;
        }
        else
        {
            fact.Result = decision.Result;
            fact.State = GameplayFactState.Completed;
            fact.FailureCode = decision.FailureCode;
            context.Competition.LeaderboardDirty = true;
            await events.RecordAsync(new(
                fact.CompetitionId,
                CompetitionEventKind.ScoringRecorded,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                result.OccurredAt,
                TeamId: fact.TeamId,
                CompetitionChallengeId: fact.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind,
                GameplayFactState: fact.State,
                GameplayFactResult: fact.Result,
                RuntimeGeneration: runtime.Generation), ct);
        }
        fact.UpdatedAt = DateTimeOffset.UtcNow;
        runtime.AwdpFixStage = AwdpFixStage.Completed;
        runtime.State = RuntimeState.Stopping;
        runtime.RunnerAssignmentReleaseToken = null;
        runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
        await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            fact.State == GameplayFactState.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            result.OccurredAt,
            ActorUserId: fact.ActorUserId,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            GameplayFactResult: decision.Result,
            RuntimeState: runtime.State,
            RuntimeGeneration: runtime.Generation), ct);
        await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.AwdpFixResolved,
            fact.State == GameplayFactState.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            result.OccurredAt,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result,
            RuntimeGeneration: runtime.Generation), ct);
        await events.RecordAsync(new(
            runtime.CompetitionId,
            CompetitionEventKind.RuntimeStateChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            result.OccurredAt,
            TeamId: runtime.TeamId,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            RuntimeState: RuntimeState.Stopping,
            RuntimeGeneration: runtime.Generation), ct);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.ProcessingVersion,
            runtime.Generation,
            runtime.RunnerPool,
            runtime.RunnerId
                ?? throw new InvalidOperationException("AWDP target has no owning Runner.")));
        await QueueNextAwdpFixAttemptAsync(fact, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return configurationWasSuperseded
            ? InternalResultDisposition.Superseded
            : InternalResultDisposition.Applied;
    }

    private async Task QueueNextAwdpFixAttemptAsync(
        GameplayFact completed,
        CancellationToken ct)
    {
        var nextGameplayFactId = await db.GameplayFacts.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == completed.CompetitionId
                && candidate.CompetitionChallengeId == completed.CompetitionChallengeId
                && candidate.TeamId == completed.TeamId
                && candidate.Kind == GameplayFactKind.FixAttempt
                && candidate.State == GameplayFactState.Queued)
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(ct);
        if (nextGameplayFactId is Guid id)
            await outbox.PublishAsync(new EvaluateGameplayFact(id));
    }

    private static DateTimeOffset NextAppliedAt(
        DateTimeOffset receivedAt,
        DateTimeOffset? previousAppliedAt)
    {
        var candidate = ToPostgresPrecision(receivedAt);
        if (previousAppliedAt is not { } previous)
            return candidate;
        previous = ToPostgresPrecision(previous);
        return candidate > previous
            ? candidate
            : previous.AddTicks(TimeSpan.TicksPerMicrosecond);
    }

    private static DateTimeOffset ToPostgresPrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
