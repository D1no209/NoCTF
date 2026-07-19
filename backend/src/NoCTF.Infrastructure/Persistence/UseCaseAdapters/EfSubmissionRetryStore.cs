using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Retry;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSubmissionRetryStore(NoCtfDbContext db) : ISubmissionRetryStore
{
    public async Task<SubmissionRetryResult> RetireCurrentEventAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var submission = await db.Submissions.Include(x => x.ScoringEvent).SingleOrDefaultAsync(x => x.Id == submissionId && x.CompetitionId == competitionId, ct);
        if (submission is null) return new(false, false);
        if (submission.ScoringEvent is null) return new(true, true);
        if (!allowCorrect && submission.ScoringEvent.Result == ScoringResult.Correct) return new(true, false, "correct_submission_cannot_retry");
        var now = DateTimeOffset.UtcNow;
        submission.ScoringEvent.IsDeleted = true;
        submission.ScoringEvent.DeletedAt = now;
        submission.ScoringEvent.DeletedById = actorId;
        submission.ScoringEvent.RowVersion++;
        submission.ScoringEventId = null;
        submission.ProcessingVersion++;
        submission.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(true, true);
    }
}
