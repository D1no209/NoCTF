using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfAdminSubmissionStatusReader(NoCtfDbContext db) : IAdminSubmissionStatusReader
{
    public Task<AdminSubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        db.Submissions.AsNoTracking()
        .GroupJoin(db.ScoringEvents.AsNoTracking(), submission => submission.ScoringEventId, scoringEvent => scoringEvent.Id,
            (submission, currentEvents) => new { submission, currentEvents })
        .SelectMany(item => item.currentEvents.DefaultIfEmpty(), (item, scoringEvent) => new { item.submission, scoringEvent })
        .Where(item => item.submission.CompetitionId == competitionId && item.submission.Id == submissionId)
        .Select(item => new AdminSubmissionStatusView(
             item.submission.Id, item.submission.CompetitionId, item.submission.TeamId, item.submission.ChallengeId,
             item.submission.Kind, item.scoringEvent == null ? null : item.scoringEvent.Result,
             item.scoringEvent == null ? null : item.scoringEvent.FailureCode, item.submission.ReceivedAt,
             item.scoringEvent == null ? null : item.scoringEvent.ProcessedAt,
             item.scoringEvent == null ? null : item.scoringEvent.ProcessedWorkerId,
             item.scoringEvent == null ? null : item.scoringEvent.EvaluatorVersion,
             item.scoringEvent != null && item.scoringEvent.IsDeleted, item.submission.ProcessingVersion))
        .SingleOrDefaultAsync(cancellationToken);
}
