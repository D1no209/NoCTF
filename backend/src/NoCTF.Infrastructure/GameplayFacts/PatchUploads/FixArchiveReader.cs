using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.PatchUploads;

namespace NoCTF.Infrastructure.GameplayFacts.PatchUploads;

public sealed class FixArchiveReader(NoCtfDbContext db) : IFixArchiveReader
{
    public Task<FixArchiveDescriptor?> FindAsync(
        Guid gameplayFactId,
        CancellationToken ct) =>
        db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.Id == gameplayFactId
                && fact.ReferenceKind == Domain.Gameplay.GameplayFactReferenceKind.PatchUpload
                && fact.ReferenceId != null)
            .Join(
                db.PatchUploads.AsNoTracking(),
                fact => fact.ReferenceId,
                upload => upload.Id,
                (fact, upload) => upload)
            .Select(upload => new FixArchiveDescriptor(
                upload.File.ObjectKey,
                upload.File.FileName,
                upload.File.ContentType))
            .SingleOrDefaultAsync(ct);
}
