using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfInternalResultStore(
    NoCtfDbContext db,
    AwdpCheckExitCodeMapper awdp,
    ITransactionalMessageOutbox outbox) : IInternalResultStore
{
    public async Task<InternalResultDisposition> RecordAwdAsync(
        AwdCheckResult result,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId, ct);
        if (runtime is null)
            return InternalResultDisposition.NotFound;
        if (runtime.Generation != result.Generation ||
            result.CheckerSequence < runtime.LastAppliedCheckerSequence ||
            result.CheckerSequence < runtime.CheckerSequence)
            return InternalResultDisposition.Superseded;
        if (result.CheckerSequence > runtime.CheckerSequence)
            return InternalResultDisposition.Conflict;
        if (runtime.LastAppliedCheckerSequence == result.CheckerSequence)
            return runtime.LastAppliedCheckerBodySha256 is not null &&
                   CryptographicOperations.FixedTimeEquals(
                       runtime.LastAppliedCheckerBodySha256,
                       result.BodySha256)
                ? InternalResultDisposition.Duplicate
                : InternalResultDisposition.Conflict;

        var revisions = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == runtime.CompetitionChallengeId)
            .Join(
                db.Competitions.AsNoTracking(),
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
        db.ScoringEvents.Add(new ScoringEvent
        {
            Id = Guid.CreateVersion7(result.OccurredAt),
            CompetitionId = runtime.CompetitionId,
            CompetitionChallengeId = runtime.CompetitionChallengeId,
            TeamId = runtime.TeamId,
            Kind = ScoringEventKind.AwdServiceStatus,
            Result = result.Up ? ScoringResult.Correct : ScoringResult.Wrong,
            ProcessingVersion = result.CheckerSequence,
            CompetitionConfigurationRevision = revisions.Competition.ConfigurationRevision,
            CompetitionChallengeRevision = revisions.ChallengeRevision,
            OccurredAt = result.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
        runtime.LastAppliedCheckerSequence = result.CheckerSequence;
        runtime.LastAppliedCheckerBodySha256 = result.BodySha256;
        revisions.Competition.LeaderboardRevision =
            checked(revisions.Competition.LeaderboardRevision + 1);
        await outbox.PublishAsync(new ProjectLeaderboard(runtime.CompetitionId));
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

        var decision = awdp.Map(result.ExitCode, result.TimedOut);
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
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return InternalResultDisposition.Applied;
    }
}
