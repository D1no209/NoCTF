using NoCTF.Application.Storage;

namespace NoCTF.Application.Submissions.Processing;

public sealed record FixArchiveCleanupItem(Guid UploadId, string ObjectKey, long RowVersion);

public interface IFixArchiveCleanupStore
{
    Task<IReadOnlyList<FixArchiveCleanupItem>> ClaimAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid uploadId, long expectedRowVersion, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record FixArchiveCleanupResult(int DeletedCount, int FailedCount);

public sealed class CleanupFixArchives(IFixArchiveCleanupStore store, IObjectStorage storage)
{
    public async Task<FixArchiveCleanupResult> ExecuteAsync(
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var items = await store.ClaimAsync(batchSize, now, cancellationToken);
        var deleted = 0;
        var failed = 0;
        foreach (var item in items)
        {
            try
            {
                await storage.DeleteAsync(item.ObjectKey, cancellationToken);
                if (await store.CompleteAsync(item.UploadId, item.RowVersion, DateTimeOffset.UtcNow, cancellationToken))
                    deleted++;
                else
                    failed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
            }
        }
        return new(deleted, failed);
    }
}
