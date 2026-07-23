using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.PatchUploads;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixArchiveReader(NoCtfDbContext db) : IFixArchiveReader
{
    public Task<FixArchiveDescriptor?> FindAsync(
        Guid submissionId,
        CancellationToken ct) =>
        db.PatchUploads.AsNoTracking()
            .Where(upload => upload.SubmissionId == submissionId)
            .Select(upload => new FixArchiveDescriptor(
                upload.ObjectKey,
                upload.OriginalFileName,
                upload.ContentType))
            .SingleOrDefaultAsync(ct);
}
