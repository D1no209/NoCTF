using NoCTF.PluginBase;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API;

internal static class StorageUrlResolver
{
    public static async Task<string?> ResolveAsync(
        IStorageProvider storage,
        string? storageKey,
        string? legacyUrl,
        CancellationToken ct)
    {
        return string.IsNullOrWhiteSpace(storageKey)
            ? legacyUrl
            : await storage.GetUrlAsync(storageKey, ct);
    }

    public static async Task ResolveAsync(
        Endpoints.Admin.CompetitionChallengeAdminDto dto,
        IStorageProvider storage,
        Core.Challenge challenge,
        CancellationToken ct)
    {
        dto.AttachmentUrl = await ResolveAsync(storage, challenge.AttachmentStorageKey, dto.AttachmentUrl, ct);
        dto.PatchTemplateUrl = await ResolveAsync(storage, challenge.PatchTemplateStorageKey, dto.PatchTemplateUrl, ct);
    }
}

internal static class StorageObjectCleanup
{
    public static async Task DeleteUnreferencedAsync(
        ApplicationDbContext db,
        IStorageProvider storage,
        IEnumerable<string?> keys,
        CancellationToken ct)
    {
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!).Distinct(StringComparer.Ordinal))
        {
            var referencedByChallenge = await db.Challenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(c => c.AttachmentStorageKey == key || c.PatchTemplateStorageKey == key, ct);
            var referencedByTemplate = await db.ChallengeTemplates
                .AsNoTracking()
                .AnyAsync(c => c.AttachmentStorageKey == key || c.PatchTemplateStorageKey == key, ct);
            if (!referencedByChallenge && !referencedByTemplate)
                await storage.DeleteAsync(key, ct);
        }
    }
}
