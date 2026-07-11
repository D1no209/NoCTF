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

    public static async Task<bool> ReferencesObjectAsync(
        IStorageProvider storage,
        string? storageKey,
        string? requestedUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || string.IsNullOrWhiteSpace(requestedUrl))
            return false;

        var currentUrl = await storage.GetUrlAsync(storageKey, ct);
        return IsSameResourceUrl(currentUrl, requestedUrl);
    }

    internal static bool IsSameResourceUrl(string left, string right)
    {
        if (Uri.TryCreate(left, UriKind.Absolute, out var leftAbsolute) &&
            Uri.TryCreate(right, UriKind.Absolute, out var rightAbsolute))
        {
            return string.Equals(leftAbsolute.Scheme, rightAbsolute.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(leftAbsolute.IdnHost, rightAbsolute.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                   leftAbsolute.Port == rightAbsolute.Port &&
                   string.Equals(leftAbsolute.AbsolutePath, rightAbsolute.AbsolutePath, StringComparison.Ordinal);
        }

        if (Uri.TryCreate(left, UriKind.Absolute, out _) || Uri.TryCreate(right, UriKind.Absolute, out _))
            return false;

        return string.Equals(ResourcePath(left), ResourcePath(right), StringComparison.Ordinal);
    }

    private static string ResourcePath(string value)
    {
        var end = value.IndexOfAny(['?', '#']);
        return end < 0 ? value : value[..end];
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
