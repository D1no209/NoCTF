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
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == result.GameplayFactId,
            ct);
        if (fact is null)
            return InternalResultDisposition.NotFound;
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null)
            return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed)
            return InternalResultDisposition.Duplicate;
        var appliedAt = ToPostgresPrecision(result.OccurredAt);

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
        if (context.Competition.Mode != GameMode.Awd
            || runtime.TeamId is null
            || fact.Kind != GameplayFactKind.AwdServiceTransition
            || fact.CompetitionId != runtime.CompetitionId
            || fact.CompetitionChallengeId != runtime.CompetitionChallengeId
            || fact.TeamId != runtime.TeamId)
            return InternalResultDisposition.NotFound;
        var scoringResult = result.State switch
        {
            AwdServiceState.Up => GameplayFactResult.ServiceUp,
            AwdServiceState.Down => GameplayFactResult.ServiceDown,
            _ => GameplayFactResult.ServiceDown
        };
        fact.State = GameplayFactState.Completed;
        fact.Result = scoringResult;
        fact.FailureCode = null;
        fact.UpdatedAt = appliedAt;
        await events.RecordAsync(new(
            runtime.CompetitionId,
            CompetitionEventKind.ScoringRecorded,
            scoringResult == GameplayFactResult.ServiceDown
                ? CompetitionEventLevel.Warning
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            appliedAt,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: GameplayFactKind.AwdServiceTransition,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result,
            RuntimeState: runtime.State), ct);
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
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed)
            return InternalResultDisposition.Duplicate;
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
                (scope, _) => new { scope.Challenge, scope.Competition })
            .SingleAsync(ct);
        if (context.Competition.Mode != GameMode.Awdp || fact.Kind != GameplayFactKind.FixAttempt)
            return InternalResultDisposition.NotFound;
        var decision = AwdpFixOutcomeMapper.Map(result.Outcome);
        if (decision.Result is null)
        {
            fact.State = GameplayFactState.PlatformFailed;
            fact.FailureCode = decision.FailureCode
                ?? GameplayFactFailureCode.CheckerPlatformError;
        }
        else
        {
            fact.Result = decision.Result;
            fact.State = GameplayFactState.Completed;
            fact.FailureCode = decision.FailureCode;
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
                GameplayFactResult: fact.Result), ct);
        }
        fact.UpdatedAt = DateTimeOffset.UtcNow;
        runtime.State = RuntimeState.Stopping;
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
            RuntimeState: runtime.State), ct);
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
            GameplayFactResult: fact.Result), ct);
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
            RuntimeState: RuntimeState.Stopping), ct);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.RunnerId
                ?? throw new InvalidOperationException("AWDP target has no owning Runner.")));
        await QueueNextAwdpFixAttemptAsync(fact, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return InternalResultDisposition.Applied;
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

    private static DateTimeOffset ToPostgresPrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
