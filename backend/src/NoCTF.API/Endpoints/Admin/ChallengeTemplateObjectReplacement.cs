using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.API.Endpoints.Admin;

internal enum ChallengeTemplateObjectSlot
{
    Attachment,
    PatchTemplate
}

internal sealed class ChallengeTemplateNotFoundException(Guid challengeId)
    : InvalidOperationException($"Challenge template {challengeId} no longer exists.");

internal static class ChallengeTemplateObjectReplacement
{
    private const int MaxCompareAndSwapAttempts = 8;
    private static readonly SemaphoreSlim[] NonRelationalLocks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public static Task ReplaceAsync(
        ApplicationDbContext db,
        Guid challengeId,
        ChallengeTemplateObjectSlot slot,
        string storageKey,
        string url,
        CancellationToken ct)
        => db.Database.IsRelational()
            ? ReplaceRelationalAsync(db, challengeId, slot, storageKey, url, ct)
            : ReplaceNonRelationalAsync(db, challengeId, slot, storageKey, url, ct);

    private static async Task ReplaceRelationalAsync(
        ApplicationDbContext db,
        Guid challengeId,
        ChallengeTemplateObjectSlot slot,
        string storageKey,
        string url,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxCompareAndSwapAttempts; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var snapshot = await db.ChallengeTemplates
                .AsNoTracking()
                .Where(challenge => challenge.Id == challengeId)
                .Select(challenge => new
                {
                    challenge.AttachmentStorageKey,
                    challenge.PatchTemplateStorageKey
                })
                .SingleOrDefaultAsync(ct)
                ?? throw new ChallengeTemplateNotFoundException(challengeId);

            var previousKey = slot == ChallengeTemplateObjectSlot.Attachment
                ? snapshot.AttachmentStorageKey
                : snapshot.PatchTemplateStorageKey;
            var now = DateTime.UtcNow;
            var updated = slot == ChallengeTemplateObjectSlot.Attachment
                ? await db.ChallengeTemplates
                    .Where(challenge =>
                        challenge.Id == challengeId &&
                        challenge.AttachmentStorageKey == previousKey)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(challenge => challenge.AttachmentUrl, url)
                        .SetProperty(challenge => challenge.AttachmentStorageKey, storageKey)
                        .SetProperty(
                            challenge => challenge.DeploymentType,
                            challenge => challenge.DeploymentType == ChallengeDeploymentType.NoAttachment
                                ? ChallengeDeploymentType.StaticAttachment
                                : challenge.DeploymentType)
                        .SetProperty(challenge => challenge.UpdatedAt, now), ct)
                : await db.ChallengeTemplates
                    .Where(challenge =>
                        challenge.Id == challengeId &&
                        challenge.PatchTemplateStorageKey == previousKey)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(challenge => challenge.PatchTemplateUrl, url)
                        .SetProperty(challenge => challenge.PatchTemplateStorageKey, storageKey)
                        .SetProperty(challenge => challenge.UpdatedAt, now), ct);

            if (updated == 0)
            {
                await transaction.RollbackAsync(ct);
                continue;
            }

            await StorageCleanupOutbox.EnqueueAsync(db, [previousKey], ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }

        throw new DbUpdateConcurrencyException(
            $"Challenge template {challengeId} was replaced too frequently to complete the upload safely.");
    }

    private static async Task ReplaceNonRelationalAsync(
        ApplicationDbContext db,
        Guid challengeId,
        ChallengeTemplateObjectSlot slot,
        string storageKey,
        string url,
        CancellationToken ct)
    {
        var gate = NonRelationalLocks[(int)((uint)challengeId.GetHashCode() % NonRelationalLocks.Length)];
        await gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            var challenge = await db.ChallengeTemplates.SingleOrDefaultAsync(
                candidate => candidate.Id == challengeId,
                ct) ?? throw new ChallengeTemplateNotFoundException(challengeId);
            string? previousKey;
            if (slot == ChallengeTemplateObjectSlot.Attachment)
            {
                previousKey = challenge.AttachmentStorageKey;
                challenge.AttachmentStorageKey = storageKey;
                challenge.AttachmentUrl = url;
                if (challenge.DeploymentType == ChallengeDeploymentType.NoAttachment)
                    challenge.DeploymentType = ChallengeDeploymentType.StaticAttachment;
            }
            else
            {
                previousKey = challenge.PatchTemplateStorageKey;
                challenge.PatchTemplateStorageKey = storageKey;
                challenge.PatchTemplateUrl = url;
            }

            challenge.UpdatedAt = DateTime.UtcNow;
            await StorageCleanupOutbox.EnqueueAsync(db, [previousKey], ct);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }
}
