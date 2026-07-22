using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Storage;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfFixArchiveDownloadStore(NoCtfDbContext db) : IFixArchiveDownloadStore
{
    public async Task<AuthorizedFixArchive?> AuthorizeAsync(
        Guid uploadId, Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var record = await db.FixSubmissionRecords.AsNoTracking()
            .Where(item => item.UploadId == uploadId
                           && item.SubmissionId == submissionId
                           && item.ExpiresAt > now
                           && item.VerificationStatus == FixVerificationStatus.Verifying)
            .Select(item => new { item.UploadId, item.SubmissionId, item.ObjectKey, item.ObjectMetadata })
            .SingleOrDefaultAsync(cancellationToken);
        if (record?.SubmissionId is not Guid id) return null;
        var metadata = string.IsNullOrWhiteSpace(record.ObjectMetadata)
            ? null
            : JsonSerializer.Deserialize<Metadata>(record.ObjectMetadata);
        return new(record.UploadId, id, record.ObjectKey,
            metadata?.FileName ?? "fix-archive", metadata?.ContentType ?? "application/octet-stream");
    }

    private sealed record Metadata(string FileName, string ContentType, long Length, string Sha256);
}
