using FluentStorage.Storage;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Authentication.Account;

public sealed record ProfileCoverReplacementResult(
    UserProfile? Profile,
    WallpaperImageFailure? Failure,
    bool UserNotFound = false)
{
    public static ProfileCoverReplacementResult Success(UserProfile profile) =>
        new(profile, null);

    public static ProfileCoverReplacementResult Rejected(WallpaperImageFailure failure) =>
        new(null, failure);

    public static ProfileCoverReplacementResult MissingUser() =>
        new(null, null, true);
}

public sealed class ReplaceCurrentUserProfileCover(
    IUserAuthenticationStore users,
    ManagedFileUploads uploads,
    IWallpaperImageProcessor imageProcessor)
{
    public async Task<ProfileCoverReplacementResult> ExecuteAsync(
        Guid userId,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        long maximumBytes,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > maximumBytes)
            return ProfileCoverReplacementResult.Rejected(WallpaperImageFailure.SizeInvalid);

        var processing = imageProcessor.Process(content);
        if (processing.Failure is not null || processing.Image is null)
        {
            return ProfileCoverReplacementResult.Rejected(
                processing.Failure ?? WallpaperImageFailure.MalformedImage);
        }

        var image = processing.Image;
        if (!MatchesSourceMetadata(fileName, contentType, image.SourceContentType))
        {
            return ProfileCoverReplacementResult.Rejected(
                WallpaperImageFailure.SourceMetadataMismatch);
        }

        var fileId = Guid.CreateVersion7(now);
        var objectKey = $"users/{userId:N}/profile-covers/{fileId:N}.{image.Extension}";
        ManagedFileUpload uploaded;
        await using (var stream = new MemoryStream(image.Content.ToArray(), writable: false))
        {
            uploaded = await uploads.CreateAsync(
                fileId,
                objectKey,
                $"profile-cover.{image.Extension}",
                image.ContentType,
                stream,
                now,
                ct);
        }

        UserProfileCoverReplacement? replacement;
        var attached = false;
        try
        {
            replacement = await users.ReplaceProfileCoverAsync(
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
            ? ProfileCoverReplacementResult.MissingUser()
            : ProfileCoverReplacementResult.Success(replacement.Profile);
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

public sealed record UserProfileCoverContent(Stream Content, string ContentType);

public sealed class GetPublicUserProfileCover(
    IUserAuthenticationStore users,
    IStore objects)
{
    public async Task<UserProfileCoverContent?> ExecuteAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var file = await users.GetProfileCoverFileAsync(userId, ct);
        if (file is null)
            return null;

        var content = await objects.OpenRead(file.ObjectKey, ct);
        return content is null ? null : new(content, file.ContentType);
    }
}
