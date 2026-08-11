using NoCTF.Application.Common;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Authentication.Account;

public static class UserProfileRules
{
    public const int MaximumDescriptionLength = 500;
    public const int AvatarOutputSize = 512;
    public const int MaximumAvatarDimension = 8192;
    public const long MaximumAvatarPixels = 32_000_000;
}

public enum AvatarImageFailure
{
    SizeInvalid,
    SourceMetadataMismatch,
    UnsupportedFormat,
    InvalidDimensions,
    PixelLimitExceeded,
    MultipleFrames,
    MalformedImage
}

public sealed record NormalizedAvatarImage(
    ReadOnlyMemory<byte> Content,
    string ContentType,
    string Extension,
    string SourceContentType);

public sealed record AvatarImageProcessingResult(
    NormalizedAvatarImage? Image,
    AvatarImageFailure? Failure)
{
    public static AvatarImageProcessingResult Success(NormalizedAvatarImage image) =>
        new(image, null);

    public static AvatarImageProcessingResult Rejected(AvatarImageFailure failure) =>
        new(null, failure);
}

public interface IAvatarImageProcessor
{
    AvatarImageProcessingResult Process(ReadOnlyMemory<byte> content);
}

public sealed record AvatarReplacementResult(
    UserProfile? Profile,
    AvatarImageFailure? Failure,
    bool UserNotFound = false)
{
    public static AvatarReplacementResult Success(UserProfile profile) =>
        new(profile, null);

    public static AvatarReplacementResult Rejected(AvatarImageFailure failure) =>
        new(null, failure);

    public static AvatarReplacementResult MissingUser() =>
        new(null, null, true);
}

public sealed class UpdateCurrentUserProfile(IUserAuthenticationStore users)
{
    public Task<UserProfile?> ExecuteAsync(
        Guid userId,
        string? description,
        bool isEmailPublic,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        return users.UpdateProfileAsync(userId, normalizedDescription, isEmailPublic, now, ct);
    }
}

public sealed class ReplaceCurrentUserAvatar(
    IUserAuthenticationStore users,
    ManagedFileUploads uploads,
    IAvatarImageProcessor imageProcessor)
{
    public async Task<AvatarReplacementResult> ExecuteAsync(
        Guid userId,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        DateTimeOffset now,
        CancellationToken ct = default)
        => await ExecuteAsync(
            userId,
            fileName,
            contentType,
            content,
            FileUploadLimits.Default.MaximumAvatarBytes,
            now,
            ct);

    public async Task<AvatarReplacementResult> ExecuteAsync(
        Guid userId,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        long maximumBytes,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > maximumBytes)
            return AvatarReplacementResult.Rejected(AvatarImageFailure.SizeInvalid);

        var processing = imageProcessor.Process(content);
        if (processing.Failure is not null || processing.Image is null)
            return AvatarReplacementResult.Rejected(
                processing.Failure ?? AvatarImageFailure.MalformedImage);
        var image = processing.Image;
        if (!MatchesSourceMetadata(fileName, contentType, image.SourceContentType))
            return AvatarReplacementResult.Rejected(AvatarImageFailure.SourceMetadataMismatch);

        var fileId = Guid.CreateVersion7(now);
        var objectKey = $"users/{userId:N}/avatars/{fileId:N}.{image.Extension}";
        ManagedFileUpload uploaded;
        await using (var stream = new MemoryStream(image.Content.ToArray(), writable: false))
        {
            uploaded = await uploads.CreateAsync(
                fileId,
                objectKey,
                $"avatar.{image.Extension}",
                image.ContentType,
                stream,
                now,
                ct);
        }

        UserAvatarReplacement? replacement;
        var attached = false;
        try
        {
            replacement = await users.ReplaceAvatarAsync(
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
            ? AvatarReplacementResult.MissingUser()
            : AvatarReplacementResult.Success(replacement.Profile);
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

public sealed record UserAvatarContent(Stream Content, string ContentType);

public sealed class GetUserAvatar(
    IUserAuthenticationStore users,
    IObjectStorage objects)
{
    public async Task<UserAvatarContent?> ExecuteAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var file = await users.GetAvatarFileAsync(userId, ct);
        if (file is null)
            return null;

        var metadata = await objects.InspectAsync(file.ObjectKey, ct);
        if (metadata is null)
            return null;

        return new(await objects.OpenReadAsync(file.ObjectKey, ct), file.ContentType);
    }
}
