using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixArchiveCleanupStore(NoCtfDbContext db) : IFixArchiveCleanupStore
{
    public async Task<IReadOnlyList<FixArchiveCleanupItem>> ClaimAsync(
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var records = await db.FixSubmissionRecords
            .Where(record => record.VerificationStatus == FixVerificationStatus.CleanupPending
                             || record.VerificationStatus == FixVerificationStatus.Expired
                             || record.SubmissionId != null
                             && db.Submissions.Any(submission => submission.Id == record.SubmissionId && submission.ScoringEventId != null)
                             && (record.VerificationStatus == FixVerificationStatus.Valid
                                 || record.VerificationStatus == FixVerificationStatus.TeamFailure
                                 || record.VerificationStatus == FixVerificationStatus.PlatformFailed))
            .OrderBy(record => record.UpdatedAt)
            .ThenBy(record => record.UploadId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var record in records.Where(record => record.VerificationStatus != FixVerificationStatus.CleanupPending))
        {
            record.VerificationStatus = FixVerificationStatus.CleanupPending;
            record.UpdatedAt = now;
            record.RowVersion++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return records.Select(record => new FixArchiveCleanupItem(record.UploadId, record.ObjectKey, record.RowVersion)).ToArray();
    }

    public async Task<bool> CompleteAsync(
        Guid uploadId,
        long expectedRowVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var changed = await db.FixSubmissionRecords
            .Where(record => record.UploadId == uploadId
                             && record.RowVersion == expectedRowVersion
                             && record.VerificationStatus == FixVerificationStatus.CleanupPending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(record => record.VerificationStatus, FixVerificationStatus.Cleaned)
                .SetProperty(record => record.UpdatedAt, now)
                .SetProperty(record => record.RowVersion, expectedRowVersion + 1), cancellationToken);
        return changed == 1;
    }
}
