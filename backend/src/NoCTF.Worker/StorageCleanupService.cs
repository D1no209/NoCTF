using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Worker;

/// <summary>
/// Deletes unreferenced storage objects from a durable outbox. A grace period
/// lets concurrent reference changes settle; every attempt re-checks all known
/// reference owners before touching the external object store.
/// </summary>
public sealed class StorageCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<StorageCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Storage cleanup sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    internal async Task<int> CleanupBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageProvider>();
        var now = DateTime.UtcNow;
        var candidates = await db.StorageCleanupItems
            .AsNoTracking()
            .Where(item =>
                item.NotBefore <= now &&
                (item.LockedUntil == null || item.LockedUntil < now))
            .OrderBy(item => item.NotBefore)
            .ThenBy(item => item.CreatedAt)
            .Take(BatchSize)
            .Select(item => new CleanupCandidate(item.Id, item.StorageKey))
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var referenced = await LoadReferencedKeysAsync(
            db,
            candidates.Select(candidate => candidate.StorageKey).ToArray(),
            ct);
        var completed = 0;
        foreach (var candidate in candidates)
        {
            db.ChangeTracker.Clear();
            var owner = Guid.NewGuid().ToString("N");
            var item = await TryClaimAsync(db, candidate.Id, owner, now, ct);
            if (item is null)
                continue;

            try
            {
                if (!referenced.Contains(item.StorageKey))
                    await storage.DeleteAsync(item.StorageKey, ct);

                db.StorageCleanupItems.Remove(item);
                await db.SaveChangesAsync(ct);
                completed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await ReleaseAfterFailureAsync(db, item.Id, owner, "cleanup_cancelled", CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Storage cleanup for object {StorageKey} failed and will be retried.",
                    item.StorageKey);
                await ReleaseAfterFailureAsync(db, item.Id, owner, SafeError(ex), CancellationToken.None);
            }
        }

        return completed;
    }

    private static async Task<HashSet<string>> LoadReferencedKeysAsync(
        ApplicationDbContext db,
        string[] candidates,
        CancellationToken ct)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var challengeReferences = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(challenge =>
                (challenge.AttachmentStorageKey != null && candidates.Contains(challenge.AttachmentStorageKey)) ||
                (challenge.PatchTemplateStorageKey != null && candidates.Contains(challenge.PatchTemplateStorageKey)))
            .Select(challenge => new { challenge.AttachmentStorageKey, challenge.PatchTemplateStorageKey })
            .ToListAsync(ct);
        referenced.UnionWith(challengeReferences
            .SelectMany(reference => new[] { reference.AttachmentStorageKey, reference.PatchTemplateStorageKey })
            .Where(key => key is not null)
            .Select(key => key!));

        var templateReferences = await db.ChallengeTemplates
            .AsNoTracking()
            .Where(template =>
                (template.AttachmentStorageKey != null && candidates.Contains(template.AttachmentStorageKey)) ||
                (template.PatchTemplateStorageKey != null && candidates.Contains(template.PatchTemplateStorageKey)))
            .Select(template => new { template.AttachmentStorageKey, template.PatchTemplateStorageKey })
            .ToListAsync(ct);
        referenced.UnionWith(templateReferences
            .SelectMany(reference => new[] { reference.AttachmentStorageKey, reference.PatchTemplateStorageKey })
            .Where(key => key is not null)
            .Select(key => key!));
        referenced.UnionWith(await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(submission => candidates.Contains(submission.PatchArchiveUrl))
            .Select(submission => submission.PatchArchiveUrl)
            .Distinct()
            .ToListAsync(ct));
        return referenced;
    }

    private static async Task<StorageCleanupItem?> TryClaimAsync(
        ApplicationDbContext db,
        Guid id,
        string owner,
        DateTime now,
        CancellationToken ct)
    {
        var lockedUntil = now.Add(ClaimDuration);
        if (db.Database.IsRelational())
        {
            var claimed = await db.StorageCleanupItems
                .Where(item =>
                    item.Id == id &&
                    item.NotBefore <= now &&
                    (item.LockedUntil == null || item.LockedUntil < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LockOwner, owner)
                    .SetProperty(item => item.LockedUntil, lockedUntil)
                    .SetProperty(item => item.UpdatedAt, now), ct);
            if (claimed != 1)
                return null;

            return await db.StorageCleanupItems.SingleAsync(
                item => item.Id == id && item.LockOwner == owner,
                ct);
        }

        var item = await db.StorageCleanupItems.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.NotBefore <= now &&
            (candidate.LockedUntil == null || candidate.LockedUntil < now), ct);
        if (item is null)
            return null;
        item.LockOwner = owner;
        item.LockedUntil = lockedUntil;
        item.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return item;
    }

    private static async Task ReleaseAfterFailureAsync(
        ApplicationDbContext db,
        Guid id,
        string owner,
        string error,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var item = await db.StorageCleanupItems.SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.LockOwner == owner,
            ct);
        if (item is null)
            return;

        item.AttemptCount++;
        item.LastError = error;
        item.NotBefore = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(item.AttemptCount, 6))));
        item.LockOwner = null;
        item.LockedUntil = null;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string SafeError(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ");
        return message.Length <= 512 ? message : message[..512];
    }

    private sealed record CleanupCandidate(Guid Id, string StorageKey);
}
