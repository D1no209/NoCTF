using NoCTF.Application.Storage;

namespace NoCTF.Application.Administration.PlatformConfiguration;

public static class PlatformConfigurationRules
{
    public const int MaximumNameLength = 100;
    public const int MaximumDescriptionLength = 500;
    public const int MaximumLogoBytes = 5 * 1024 * 1024;
}

public sealed record PlatformConfigurationView(
    string Name,
    string? Description,
    Guid? LogoFileId,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record PlatformLogoReplacement(
    PlatformConfigurationView Configuration,
    Guid? PreviousFileId);

public interface IPlatformConfigurationStore
{
    Task<PlatformConfigurationView> GetAsync(CancellationToken cancellationToken);

    Task<PlatformConfigurationView?> UpdateAsync(
        string name,
        string? description,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<PlatformLogoReplacement?> ReplaceLogoAsync(
        StoredObject storedObject,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<BusinessFileReference?> GetLogoFileAsync(CancellationToken cancellationToken);
}

public enum PlatformConfigurationUpdateState
{
    Updated,
    RevisionConflict,
    InvalidInput
}

public sealed record PlatformConfigurationUpdateResult(
    PlatformConfigurationUpdateState State,
    PlatformConfigurationView? Configuration = null);

public enum PlatformLogoUpdateState
{
    Updated,
    RevisionConflict,
    InvalidSize,
    InvalidFormat
}

public sealed record PlatformLogoUpdateResult(
    PlatformLogoUpdateState State,
    PlatformConfigurationView? Configuration = null);

public sealed record PlatformLogoContent(Stream Content, string ContentType);

public sealed class ManagePlatformConfiguration(
    IPlatformConfigurationStore settings,
    IObjectStorage objects)
{
    public Task<PlatformConfigurationView> GetAsync(CancellationToken ct = default) =>
        settings.GetAsync(ct);

    public async Task<PlatformConfigurationUpdateResult> UpdateAsync(
        string name,
        string? description,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizedName = name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        if (normalizedName.Length is < 1 or > PlatformConfigurationRules.MaximumNameLength
            || normalizedDescription?.Length > PlatformConfigurationRules.MaximumDescriptionLength)
            return new(PlatformConfigurationUpdateState.InvalidInput);

        var updated = await settings.UpdateAsync(
            normalizedName,
            normalizedDescription,
            expectedRevision,
            now,
            ct);
        return updated is null
            ? new(PlatformConfigurationUpdateState.RevisionConflict)
            : new(PlatformConfigurationUpdateState.Updated, updated);
    }

    public async Task<PlatformLogoUpdateResult> ReplaceLogoAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > PlatformConfigurationRules.MaximumLogoBytes)
            return new(PlatformLogoUpdateState.InvalidSize);

        var image = DetectImage(content.Span);
        if (image is null
            || !string.Equals(contentType, image.Value.ContentType, StringComparison.OrdinalIgnoreCase))
            return new(PlatformLogoUpdateState.InvalidFormat);

        var objectKey = $"platform/logo/{Guid.CreateVersion7(now):N}.{image.Value.Extension}";
        StoredObject stored;
        await using (var stream = new MemoryStream(content.ToArray(), writable: false))
        {
            stored = await objects.PutAsync(
                objectKey,
                fileName,
                image.Value.ContentType,
                stream,
                ct);
        }

        PlatformLogoReplacement? replacement;
        try
        {
            replacement = await settings.ReplaceLogoAsync(
                stored,
                expectedRevision,
                now,
                ct);
        }
        catch
        {
            await TryDeleteAsync(stored.ObjectKey);
            throw;
        }

        if (replacement is null)
        {
            await TryDeleteAsync(stored.ObjectKey);
            return new(PlatformLogoUpdateState.RevisionConflict);
        }

        return new(PlatformLogoUpdateState.Updated, replacement.Configuration);
    }

    public async Task<PlatformLogoContent?> GetLogoAsync(CancellationToken ct = default)
    {
        var file = await settings.GetLogoFileAsync(ct);
        if (file is null)
            return null;

        var metadata = await objects.InspectAsync(file.ObjectKey, ct);
        if (metadata is null)
            return null;

        return new(
            await objects.OpenReadAsync(file.ObjectKey, ct),
            file.ContentType);
    }

    private async Task TryDeleteAsync(string objectKey)
    {
        try
        {
            await objects.DeleteAsync(objectKey, CancellationToken.None);
        }
        catch
        {
            // The database remains authoritative; orphan cleanup is best effort.
        }
    }

    private static RasterImage? DetectImage(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8
            && content[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return new("image/png", "png");
        if (content.Length >= 3
            && content[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }))
            return new("image/jpeg", "jpg");
        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content.Slice(8, 4).SequenceEqual("WEBP"u8))
            return new("image/webp", "webp");
        return null;
    }

    private readonly record struct RasterImage(string ContentType, string Extension);
}
