using System.Runtime.ExceptionServices;
using NoCTF.Infrastructure;

namespace NoCTF.API;

internal static class UploadedObjectPersistence
{
    public static async Task<TResult> CompleteAsync<TResult>(
        ApplicationDbContext db,
        string storageKey,
        Func<CancellationToken, Task<TResult>> persist,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            return await persist(ct);
        }
        catch (Exception persistenceException)
        {
            try
            {
                // SaveChanges can fail after the database has committed. Queue
                // cleanup durably and let the worker re-check references instead
                // of risking deletion of an object that is already referenced.
                db.ChangeTracker.Clear();
                await StorageObjectCleanup.EnqueueAsync(db, [storageKey], CancellationToken.None);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError(
                    cleanupException,
                    "Failed to queue uploaded object {StorageKey} for cleanup after persistence failure.",
                    storageKey);
            }

            ExceptionDispatchInfo.Capture(persistenceException).Throw();
            throw;
        }
    }
}
