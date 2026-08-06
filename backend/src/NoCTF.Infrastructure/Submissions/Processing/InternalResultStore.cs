using NoCTF.Infrastructure.Persistence;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Submissions.Processing;

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

        var revisions = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == runtime.CompetitionChallengeId)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new
                {
                    Competition = competition,
                    ChallengeRevision = challenge.Revision
                })
            .SingleAsync(ct);
        if (revisions.Competition.Mode != GameMode.Awd || runtime.TeamId is null)
            return InternalResultDisposition.NotFound;
        var latestServiceResult = await db.ScoringEvents.AsNoTracking()
            .Where(scoringEvent => scoringEvent.CompetitionChallengeId == runtime.CompetitionChallengeId
                && scoringEvent.TeamId == runtime.TeamId
                && scoringEvent.Kind == ScoringEventKind.AwdServiceStatus)
            .OrderByDescending(scoringEvent => scoringEvent.OccurredAt)
            .ThenByDescending(scoringEvent => scoringEvent.CreatedAt)
            .Select(scoringEvent => (ScoringResult?)scoringEvent.Result)
            .FirstOrDefaultAsync(ct);
        var currentState = latestServiceResult == ScoringResult.Wrong
            ? AwdServiceState.Down
            : AwdServiceState.Up;
        var transitionResult = AwdServiceStateTransition.ToScoringResult(
            currentState,
            result.State);
        if (transitionResult is ScoringResult scoringResult)
        {
            var scoringEvent = new ScoringEvent
            {
                Id = Guid.CreateVersion7(appliedAt),
                CompetitionId = runtime.CompetitionId,
                CompetitionChallengeId = runtime.CompetitionChallengeId,
                TeamId = runtime.TeamId,
                Kind = ScoringEventKind.AwdServiceStatus,
                Result = scoringResult,
                CompetitionConfigurationRevision = revisions.Competition.ConfigurationRevision,
                CompetitionChallengeRevision = revisions.ChallengeRevision,
                OccurredAt = appliedAt,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ScoringEvents.Add(scoringEvent);
            await events.RecordAsync(new(
                runtime.CompetitionId,
                CompetitionEventKind.ScoringRecorded,
                scoringResult == ScoringResult.Wrong
                    ? CompetitionEventLevel.Warning
                    : CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                appliedAt,
                TeamId: runtime.TeamId,
                CompetitionChallengeId: runtime.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                ScoringEventKind: scoringEvent.Kind,
                ScoringResult: scoringEvent.Result,
                RuntimeState: runtime.State,
                RuntimeGeneration: runtime.Generation), ct);
            await LeaderboardRevision.IncrementAsync(
                db,
                runtime.CompetitionId,
                ct);
            await outbox.PublishAsync(new InvalidateLeaderboard(runtime.CompetitionId));
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
        var submission = await db.Submissions.SingleOrDefaultAsync(
            item => item.Id == result.SubmissionId, ct);
        if (submission is null)
            return InternalResultDisposition.NotFound;
        if (submission.ProcessingVersion > result.ProcessingVersion)
            return InternalResultDisposition.Superseded;
        if (submission.ProcessingVersion < result.ProcessingVersion)
            return InternalResultDisposition.Conflict;
        if (submission.EvaluationResultBodySha256 is not null)
            return CryptographicOperations.FixedTimeEquals(
                submission.EvaluationResultBodySha256,
                result.BodySha256)
                ? InternalResultDisposition.Duplicate
                : InternalResultDisposition.Conflict;
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null
            || runtime.Purpose != RuntimePurpose.AwdpTarget
            || runtime.SubmissionId != submission.Id)
            return InternalResultDisposition.NotFound;
        if (runtime.Generation != result.Generation
            || runtime.ProcessingVersion > result.RuntimeProcessingVersion)
            return InternalResultDisposition.Superseded;
        if (runtime.ProcessingVersion < result.RuntimeProcessingVersion)
            return InternalResultDisposition.Conflict;
        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == submission.CompetitionChallengeId)
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
        if (context.Competition.Mode != GameMode.Awdp || submission.Kind != SubmissionKind.Fix)
            return InternalResultDisposition.NotFound;
        var configurationWasSuperseded =
            runtime.SourceCompetitionConfigurationRevision
                != context.Competition.ConfigurationRevision
            || runtime.SourceCompetitionChallengeRevision != context.Challenge.Revision
            || runtime.SourceChallengeDefinitionRevision != context.DefinitionRevision;
        var decision = AwdpFixOutcomeMapper.Map(result.Outcome);
        if (configurationWasSuperseded
            || decision.Result == ScoringResult.PlatformFailed)
        {
            submission.EvaluationState = SubmissionEvaluationState.PlatformFailed;
            submission.EvaluationFailureCode =
                configurationWasSuperseded
                    ? ScoringFailureCode.CheckerPlatformError
                    : decision.FailureCode ?? ScoringFailureCode.CheckerPlatformError;
        }
        else
        {
            if (submission.CurrentScoringEventId is Guid currentId)
            {
                var current = await db.ScoringEvents.IgnoreQueryFilters()
                    .SingleAsync(item => item.Id == currentId, ct);
                current.DeletedAt = DateTimeOffset.UtcNow;
            }
            var scoringEvent = new ScoringEvent
            {
                Id = Guid.CreateVersion7(result.OccurredAt),
                CompetitionId = submission.CompetitionId,
                CompetitionChallengeId = submission.CompetitionChallengeId,
                SubmissionId = submission.Id,
                TeamId = submission.TeamId,
                Kind = ScoringEventKind.SubmissionEvaluation,
                Result = decision.Result,
                FailureCode = decision.FailureCode,
                ProcessingVersion = submission.ProcessingVersion,
                CompetitionConfigurationRevision = context.Competition.ConfigurationRevision,
                CompetitionChallengeRevision = context.Challenge.Revision,
                OccurredAt = result.OccurredAt,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ScoringEvents.Add(scoringEvent);
            submission.CurrentScoringEventId = scoringEvent.Id;
            submission.EvaluationState = SubmissionEvaluationState.Completed;
            submission.EvaluationFailureCode = null;
            await LeaderboardRevision.IncrementAsync(
                db,
                submission.CompetitionId,
                ct);
            await outbox.PublishAsync(new InvalidateLeaderboard(submission.CompetitionId));
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.ScoringRecorded,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                result.OccurredAt,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                SubmissionId: submission.Id,
                ScoringEventKind: scoringEvent.Kind,
                ScoringResult: scoringEvent.Result,
                RuntimeGeneration: runtime.Generation), ct);
        }
        submission.EvaluationResultBodySha256 = result.BodySha256;
        submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
        runtime.State = RuntimeState.Stopping;
        runtime.RunnerAssignmentReleaseToken = null;
        runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.SubmissionEvaluated,
            submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            result.OccurredAt,
            ActorUserId: submission.SubmittedByUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            SubmissionId: submission.Id,
            SubmissionKind: submission.Kind,
            SubmissionState: submission.EvaluationState,
            ScoringResult: decision.Result,
            RuntimeState: runtime.State,
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
            SubmissionId: submission.Id,
            RuntimeState: RuntimeState.Stopping,
            RuntimeGeneration: runtime.Generation), ct);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.ProcessingVersion,
            runtime.RunnerPool,
            runtime.RunnerId
                ?? throw new InvalidOperationException("AWDP target has no owning Runner.")));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return configurationWasSuperseded
            ? InternalResultDisposition.Superseded
            : InternalResultDisposition.Applied;
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
