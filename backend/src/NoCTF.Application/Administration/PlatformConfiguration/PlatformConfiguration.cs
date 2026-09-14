using FluentStorage.Storage;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Administration.PlatformConfiguration;

public static class PlatformConfigurationRules
{
    public const int MaximumNameLength = 100;
    public const int MaximumDescriptionLength = 500;
}

public sealed record PlatformConfigurationView(
    string Name,
    string? Description,
    Guid? LogoFileId,
    DateTimeOffset UpdatedAt,
    bool CtfPatchVerificationEnabled = false);

public sealed record PlatformLogoReplacement(
    PlatformConfigurationView Configuration,
    Guid? PreviousFileId);

public interface IPlatformConfigurationStore
{
    Task<PlatformConfigurationView> GetAsync(CancellationToken cancellationToken);

    Task<PlatformConfigurationView> UpdateAsync(
        string name,
        string? description,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<PlatformConfigurationView> UpdateExperimentalFeaturesAsync(
        bool ctfPatchVerificationEnabled,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<PlatformLogoReplacement> ReplaceLogoAsync(
        Guid fileId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<BusinessFileReference?> GetLogoFileAsync(CancellationToken cancellationToken);
}

public enum PlatformConfigurationUpdateState
{
    Updated,
    InvalidInput
}

public sealed record PlatformConfigurationUpdateResult(
    PlatformConfigurationUpdateState State,
    PlatformConfigurationView? Configuration = null);

public enum PlatformLogoUpdateState
{
    Updated,
    InvalidSize,
    InvalidFormat
}

public sealed record PlatformLogoUpdateResult(
    PlatformLogoUpdateState State,
    PlatformConfigurationView? Configuration = null);

public sealed record PlatformLogoContent(Stream Content, string ContentType);

public sealed class ManagePlatformConfiguration(
    IPlatformConfigurationStore settings,
    IStore objects,
    ManagedFileUploads uploads)
{
    public Task<PlatformConfigurationView> GetAsync(CancellationToken ct = default) =>
        settings.GetAsync(ct);

    public async Task<PlatformConfigurationUpdateResult> UpdateAsync(
        string name,
        string? description,
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
            now,
            ct);
        return new(PlatformConfigurationUpdateState.Updated, updated);
    }

    public Task<PlatformConfigurationView> UpdateExperimentalFeaturesAsync(
        bool ctfPatchVerificationEnabled,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        settings.UpdateExperimentalFeaturesAsync(ctfPatchVerificationEnabled, now, ct);

    public async Task<PlatformLogoUpdateResult> ReplaceLogoAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        DateTimeOffset now,
        CancellationToken ct = default)
        => await ReplaceLogoAsync(
            fileName,
            contentType,
            content,
            FileUploadLimits.Default.MaximumLogoBytes,
            now,
            ct);

    public async Task<PlatformLogoUpdateResult> ReplaceLogoAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        long maximumBytes,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > maximumBytes)
            return new(PlatformLogoUpdateState.InvalidSize);

        var image = DetectImage(content.Span);
        if (image is null
            || !string.Equals(contentType, image.Value.ContentType, StringComparison.OrdinalIgnoreCase))
            return new(PlatformLogoUpdateState.InvalidFormat);

        var fileId = Guid.CreateVersion7(now);
        var objectKey = $"platform/logo/{fileId:N}.{image.Value.Extension}";
        ManagedFileUpload uploaded;
        await using (var stream = new MemoryStream(content.ToArray(), writable: false))
        {
            uploaded = await uploads.CreateAsync(
                fileId,
                objectKey,
                fileName,
                image.Value.ContentType,
                stream,
                now,
                ct);
        }

        PlatformLogoReplacement replacement;
        var attached = false;
        try
        {
            replacement = await settings.ReplaceLogoAsync(
                uploaded.FileId,
                now,
                ct);
            attached = true;
        }
        finally
        {
            if (!attached)
                await uploads.AbandonAsync(uploaded.FileId);
        }

        return new(PlatformLogoUpdateState.Updated, replacement.Configuration);
    }

    public async Task<PlatformLogoContent?> GetLogoAsync(CancellationToken ct = default)
    {
        var file = await settings.GetLogoFileAsync(ct);
        if (file is null)
            return null;

        var content = await objects.OpenRead(file.ObjectKey, ct);
        return content is null ? null : new(content, file.ContentType);
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
