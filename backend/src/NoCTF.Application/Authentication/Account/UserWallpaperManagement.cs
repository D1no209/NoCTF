using FluentStorage.Storage;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Authentication.Account;

public static class UserWallpaperRules
{
    public const int MaximumOutputDimension = 3840;
    public const int MaximumSourceDimension = 16_384;
    public const long MaximumSourcePixels = 80_000_000;
}

public enum WallpaperImageFailure
{
    SizeInvalid,
    SourceMetadataMismatch,
    UnsupportedFormat,
    InvalidDimensions,
    PixelLimitExceeded,
    MultipleFrames,
    MalformedImage
}

public sealed record NormalizedWallpaperImage(
    ReadOnlyMemory<byte> Content,
    string ContentType,
    string Extension,
    string SourceContentType);

public sealed record WallpaperImageProcessingResult(
    NormalizedWallpaperImage? Image,
    WallpaperImageFailure? Failure)
{
    public static WallpaperImageProcessingResult Success(NormalizedWallpaperImage image) =>
        new(image, null);

    public static WallpaperImageProcessingResult Rejected(WallpaperImageFailure failure) =>
        new(null, failure);
}

public interface IWallpaperImageProcessor
{
    WallpaperImageProcessingResult Process(ReadOnlyMemory<byte> content);
}

public sealed record WallpaperReplacementResult(
    UserProfile? Profile,
    WallpaperImageFailure? Failure,
    bool UserNotFound = false)
{
    public static WallpaperReplacementResult Success(UserProfile profile) =>
        new(profile, null);

    public static WallpaperReplacementResult Rejected(WallpaperImageFailure failure) =>
        new(null, failure);

    public static WallpaperReplacementResult MissingUser() =>
        new(null, null, true);
}

public sealed class ReplaceCurrentUserWallpaper(
    IUserAuthenticationStore users,
    ManagedFileUploads uploads,
    IWallpaperImageProcessor imageProcessor)
{
    public async Task<WallpaperReplacementResult> ExecuteAsync(
        Guid userId,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        long maximumBytes,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > maximumBytes)
            return WallpaperReplacementResult.Rejected(WallpaperImageFailure.SizeInvalid);

        var processing = imageProcessor.Process(content);
        if (processing.Failure is not null || processing.Image is null)
        {
            return WallpaperReplacementResult.Rejected(
                processing.Failure ?? WallpaperImageFailure.MalformedImage);
        }

        var image = processing.Image;
        if (!MatchesSourceMetadata(fileName, contentType, image.SourceContentType))
        {
            return WallpaperReplacementResult.Rejected(
                WallpaperImageFailure.SourceMetadataMismatch);
        }

        var fileId = Guid.CreateVersion7(now);
        var objectKey = $"users/{userId:N}/wallpapers/{fileId:N}.{image.Extension}";
        ManagedFileUpload uploaded;
        await using (var stream = new MemoryStream(image.Content.ToArray(), writable: false))
        {
            uploaded = await uploads.CreateAsync(
                fileId,
                objectKey,
                $"wallpaper.{image.Extension}",
                image.ContentType,
                stream,
                now,
                ct);
        }

        UserWallpaperReplacement? replacement;
        var attached = false;
        try
        {
            replacement = await users.ReplaceWallpaperAsync(
                userId,
                uploaded.FileId,
                now,
                ct);
            attached = replacement is not null;
        }
        finally
        {
            if (!attached)
                await uploads.AbandonAsync(uploaded.FileId);
        }

        return replacement is null
            ? WallpaperReplacementResult.MissingUser()
            : WallpaperReplacementResult.Success(replacement.Profile);
    }

    private static bool MatchesSourceMetadata(
        string fileName,
        string declaredContentType,
        string actualContentType)
    {
        if (!string.Equals(declaredContentType, actualContentType, StringComparison.OrdinalIgnoreCase))
            return false;

        var extension = Path.GetExtension(fileName);
        return actualContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            "image/png" => extension.Equals(".png", StringComparison.OrdinalIgnoreCase),
            "image/webp" => extension.Equals(".webp", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

public sealed record UserWallpaperContent(Stream Content, string ContentType);

public sealed class GetCurrentUserWallpaper(
    IUserAuthenticationStore users,
    IStore objects)
{
    public async Task<UserWallpaperContent?> ExecuteAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var file = await users.GetWallpaperFileAsync(userId, ct);
        if (file is null)
            return null;

        var content = await objects.OpenRead(file.ObjectKey, ct);
        return content is null ? null : new(content, file.ContentType);
    }
}

public sealed class UpdateCurrentUserWallpaperPreference(IUserAuthenticationStore users)
{
    public Task<UserWallpaperPreferenceResult> ExecuteAsync(
        Guid userId,
        bool enabled,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        users.SetWallpaperEnabledAsync(userId, enabled, now, ct);
}
