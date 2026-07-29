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

namespace NoCTF.Infrastructure.Submissions.Processing;

public sealed class InternalResultStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : IInternalResultStore
{
    public async Task<InternalResultDisposition> RecordAwdAsync(
        AwdCheckResult result,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var runtime = await db.RuntimeInstances.FromSqlInterpolated(
                $"SELECT * FROM runtime_instances WHERE id = {result.RuntimeInstanceId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (runtime is null)
            return InternalResultDisposition.NotFound;
        if (runtime.Generation != result.Generation)
            return InternalResultDisposition.Superseded;

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
            db.ScoringEvents.Add(new ScoringEvent
            {
                Id = Guid.CreateVersion7(result.OccurredAt),
                CompetitionId = runtime.CompetitionId,
                CompetitionChallengeId = runtime.CompetitionChallengeId,
                TeamId = runtime.TeamId,
                Kind = ScoringEventKind.AwdServiceStatus,
                Result = scoringResult,
                ProcessingVersion = runtime.CheckerSequence,
                CompetitionConfigurationRevision = revisions.Competition.ConfigurationRevision,
                CompetitionChallengeRevision = revisions.ChallengeRevision,
                OccurredAt = result.OccurredAt,
                CreatedAt = DateTimeOffset.UtcNow
            });
            revisions.Competition.LeaderboardRevision =
                checked(revisions.Competition.LeaderboardRevision + 1);
            await outbox.PublishAsync(new ProjectLeaderboard(runtime.CompetitionId));
        }
        runtime.CheckerStatus = result.State;
        runtime.CheckerStatusUpdatedAt = result.OccurredAt;
        runtime.LastAppliedCheckerSequence = runtime.CheckerSequence;
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
        var runtime = await db.RuntimeInstances.FromSqlInterpolated(
                $"SELECT * FROM runtime_instances WHERE id = {result.RuntimeInstanceId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
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
            .SingleAsync(ct);
        if (context.Competition.Mode != GameMode.Awdp || submission.Kind != SubmissionKind.Fix)
            return InternalResultDisposition.NotFound;
        var decision = AwdpFixOutcomeMapper.Map(result.Outcome);
        if (decision.Result == ScoringResult.PlatformFailed)
        {
            submission.EvaluationState = SubmissionEvaluationState.PlatformFailed;
            submission.EvaluationFailureCode =
                decision.FailureCode ?? ScoringFailureCode.CheckerPlatformError;
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
            context.Competition.LeaderboardRevision =
                checked(context.Competition.LeaderboardRevision + 1);
            await outbox.PublishAsync(new ProjectLeaderboard(submission.CompetitionId));
        }
        submission.EvaluationResultBodySha256 = result.BodySha256;
        submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
        runtime.State = RuntimeState.Stopping;
        runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.ProcessingVersion,
            runtime.RunnerPool,
            runtime.RunnerId
                ?? throw new InvalidOperationException("AWDP target has no owning Runner.")));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return InternalResultDisposition.Applied;
    }
}
