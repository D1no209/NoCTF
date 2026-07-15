using Microsoft.EntityFrameworkCore;
using NoCTF.Core;

namespace NoCTF.Infrastructure.Storage;

/// <summary>
/// Adds storage objects to the durable cleanup outbox. The caller remains
/// responsible for saving changes so the outbox entry can be committed in the
/// same transaction as the reference that displaced the object.
/// </summary>
public static class StorageCleanupOutbox
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(1);

    public static async Task EnqueueAsync(
        ApplicationDbContext db,
        IEnumerable<string?> keys,
        CancellationToken ct = default)
    {
        var candidates = keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            // Older AWDP rows may contain an already-resolved legacy URL in
            // PatchArchiveUrl. It is not an object key owned by this provider.
            .Where(key => !Uri.TryCreate(key, UriKind.Absolute, out _))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
            return;

        var queued = await db.StorageCleanupItems
            .AsNoTracking()
            .Where(item => candidates.Contains(item.StorageKey))
            .Select(item => item.StorageKey)
            .ToListAsync(ct);
        var known = queued
            .Concat(db.StorageCleanupItems.Local.Select(item => item.StorageKey))
            .ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        foreach (var key in candidates.Where(key => !known.Contains(key)))
        {
            db.StorageCleanupItems.Add(new StorageCleanupItem
            {
                Id = Guid.NewGuid(),
                StorageKey = key,
                NotBefore = now.Add(GracePeriod),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }
}
