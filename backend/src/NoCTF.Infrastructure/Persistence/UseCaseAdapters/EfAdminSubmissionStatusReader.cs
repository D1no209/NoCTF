using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfAdminSubmissionStatusReader(NoCtfDbContext db) : IAdminSubmissionStatusReader
{
    public Task<AdminSubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        (from submission in db.Submissions.AsNoTracking()
         join scoringEvent in db.ScoringEvents.AsNoTracking()
             on submission.ScoringEventId equals scoringEvent.Id into currentEvents
         from scoringEvent in currentEvents.DefaultIfEmpty()
         where submission.CompetitionId == competitionId && submission.Id == submissionId
         select new AdminSubmissionStatusView(
             submission.Id,
             submission.CompetitionId,
             submission.TeamId,
             submission.ChallengeId,
             submission.Kind,
             scoringEvent == null ? null : scoringEvent.Result,
             scoringEvent == null ? null : scoringEvent.FailureCode,
             submission.ReceivedAt,
             scoringEvent == null ? null : scoringEvent.ProcessedAt,
             scoringEvent == null ? null : scoringEvent.ProcessedWorkerId,
             scoringEvent == null ? null : scoringEvent.EvaluatorVersion,
             scoringEvent != null && scoringEvent.IsDeleted,
             submission.ProcessingVersion))
        .SingleOrDefaultAsync(cancellationToken);
}
