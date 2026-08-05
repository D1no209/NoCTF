using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Status;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Submissions.Status;

public sealed class SubmissionStatusReader(NoCtfDbContext db) : ISubmissionStatusReader
{
    public async Task<SubmissionStatusView?> FindAsync(
        Guid competitionId,
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var canRead = await db.Competitions.AsNoTracking().AnyAsync(
                competition => competition.Id == competitionId
                    && (competition.OwnerId == userId
                        || competition.ManagerIds.Contains(userId)
                        || competition.JudgeIds.Contains(userId)
                        || competition.ObserverIds.Contains(userId)),
                cancellationToken)
            || await db.Teams.AsNoTracking().AnyAsync(
                team => team.CompetitionId == competitionId && team.MemberIds.Contains(userId),
                cancellationToken);
        if (!canRead)
            return null;
        return await db.Submissions.AsNoTracking()
            .Where(submission =>
                submission.Id == submissionId && submission.CompetitionId == competitionId)
            .GroupJoin(
                db.ScoringEvents.AsNoTracking(),
                submission => submission.CurrentScoringEventId,
                scoringEvent => (Guid?)scoringEvent.Id,
                (submission, scoringEvents) => new { submission, scoringEvents })
            .SelectMany(
                item => item.scoringEvents.DefaultIfEmpty(),
                (item, scoringEvent) => new
                {
                    item.submission,
                    Result = scoringEvent == null ? null : (ScoringResult?)scoringEvent.Result,
                    FailureCode = item.submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                        ? item.submission.EvaluationFailureCode
                        : scoringEvent == null ? null : scoringEvent.FailureCode
                })
            .Select(item => new SubmissionStatusView(
                item.submission.Id,
                item.submission.CompetitionId,
                item.submission.TeamId,
                item.submission.CompetitionChallengeId,
                item.submission.Kind,
                item.submission.EvaluationState,
                SubmissionResultDisclosure.PlayerResult(item.Result, item.FailureCode),
                SubmissionResultDisclosure.PlayerFailureCode(item.FailureCode),
                item.submission.ReceivedAt,
                item.submission.EvaluationUpdatedAt,
                item.submission.ProcessingVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
