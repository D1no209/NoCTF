using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Status;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Submissions.Administration;

public sealed class AdminSubmissionStatusReader(NoCtfDbContext db) : IAdminSubmissionStatusReader
{
    public Task<AdminSubmissionStatusView?> FindAsync(
        Guid competitionId,
        Guid submissionId,
        CancellationToken cancellationToken) =>
        db.Submissions.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId && submission.Id == submissionId)
            .GroupJoin(
                db.ScoringEvents.AsNoTracking(),
                submission => submission.CurrentScoringEventId,
                scoringEvent => (Guid?)scoringEvent.Id,
                (submission, scoringEvents) => new { submission, scoringEvents })
            .SelectMany(
                item => item.scoringEvents.DefaultIfEmpty(),
                (item, scoringEvent) => new AdminSubmissionStatusView(
                    item.submission.Id,
                    item.submission.CompetitionId,
                    item.submission.TeamId,
                    item.submission.CompetitionChallengeId,
                    item.submission.SubmittedByUserId,
                    item.submission.Kind,
                    item.submission.EvaluationState,
                    scoringEvent == null ? null : scoringEvent.Result,
                    item.submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                        ? item.submission.EvaluationFailureCode
                        : scoringEvent == null ? null : scoringEvent.FailureCode,
                    item.submission.ReceivedAt,
                    item.submission.EvaluationUpdatedAt,
                    item.submission.ProcessingVersion))
            .SingleOrDefaultAsync(cancellationToken);
}
