using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Retry;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSubmissionRetryStore(NoCtfDbContext db) : ISubmissionRetryStore
{
    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().Where(item => item.Id == competitionId && !item.Deletion.IsDeleted)
            .Select(item => (CompetitionStatus?)item.Status).SingleOrDefaultAsync(ct);

    public async Task<SubmissionRetryResult> RetireCurrentEventAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return new(false, false, SubmissionRetryFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished) return new(true, false, SubmissionRetryFailure.CompetitionFinished);
        var submission = await db.Submissions.Include(x => x.ScoringEvent).SingleOrDefaultAsync(x => x.Id == submissionId && x.CompetitionId == competitionId, ct);
        if (submission is null) return new(false, false);
        if (submission.ScoringEvent is null) return new(true, true);
        if (!allowCorrect && submission.ScoringEvent.Result == ScoringResult.Correct)
            return new(true, false, SubmissionRetryFailure.CorrectSubmissionCannotRetry);
        FixSubmissionRecord? fixRecord = null;
        if (submission.Kind == SubmissionKind.Fix)
        {
            fixRecord = await db.FixSubmissionRecords.SingleOrDefaultAsync(record => record.SubmissionId == submission.Id, ct);
            if (fixRecord is null || !FixVerificationStateMachine.CanResetForRetry(fixRecord.VerificationStatus))
                return new(true, false, SubmissionRetryFailure.FixVerificationCannotRetry);
        }
        var now = DateTimeOffset.UtcNow;
        submission.ScoringEvent.IsDeleted = true;
        submission.ScoringEvent.DeletedAt = now;
        submission.ScoringEvent.DeletedById = actorId;
        submission.ScoringEvent.RowVersion++;
        submission.ScoringEventId = null;
        submission.ProcessingVersion++;
        submission.UpdatedAt = now;
        if (fixRecord is not null)
        {
            fixRecord.VerificationStatus = FixVerificationStatus.Claimed;
            fixRecord.FailureCategory = null;
            fixRecord.VerifiedAt = null;
            fixRecord.VerifierVersion = null;
            fixRecord.RowVersion++;
            fixRecord.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(true, true);
    }
}
