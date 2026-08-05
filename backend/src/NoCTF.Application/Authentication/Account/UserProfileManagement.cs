using NoCTF.Application.Common;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Authentication.Account;

public static class UserProfileRules
{
    public const int MaximumDescriptionLength = 500;
    public const int MaximumAvatarBytes = 3 * 1024 * 1024;
    public const int MaximumAvatarRequestBytes = MaximumAvatarBytes + 64 * 1024;
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
    IObjectStorage objects,
    IAvatarImageProcessor imageProcessor)
{
    public async Task<AvatarReplacementResult> ExecuteAsync(
        Guid userId,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > UserProfileRules.MaximumAvatarBytes)
            return AvatarReplacementResult.Rejected(AvatarImageFailure.SizeInvalid);

        var processing = imageProcessor.Process(content);
        if (processing.Failure is not null || processing.Image is null)
            return AvatarReplacementResult.Rejected(
                processing.Failure ?? AvatarImageFailure.MalformedImage);
        var image = processing.Image;
        if (!MatchesSourceMetadata(fileName, contentType, image.SourceContentType))
            return AvatarReplacementResult.Rejected(AvatarImageFailure.SourceMetadataMismatch);

        var objectKey = $"users/{userId:N}/avatars/{Guid.CreateVersion7(now):N}.{image.Extension}";
        StoredObject stored;
        await using (var stream = new MemoryStream(image.Content.ToArray(), writable: false))
        {
            stored = await objects.PutAsync(
                objectKey,
                $"avatar.{image.Extension}",
                image.ContentType,
                stream,
                ct);
        }

        UserAvatarReplacement? replacement;
        try
        {
            replacement = await users.ReplaceAvatarAsync(userId, stored.ObjectKey, now, ct);
        }
        catch
        {
            await TryDeleteAsync(stored.ObjectKey);
            throw;
        }

        if (replacement is null)
        {
            await TryDeleteAsync(stored.ObjectKey);
            return AvatarReplacementResult.MissingUser();
        }

        if (!string.IsNullOrWhiteSpace(replacement.PreviousObjectKey)
            && !string.Equals(replacement.PreviousObjectKey, stored.ObjectKey, StringComparison.Ordinal))
            await TryDeleteAsync(replacement.PreviousObjectKey);

        return AvatarReplacementResult.Success(replacement.Profile);
    }

    private async Task TryDeleteAsync(string objectKey)
    {
        try
        {
            await objects.DeleteAsync(objectKey, CancellationToken.None);
        }
        catch
        {
            // The database remains the source of truth; orphan cleanup is best effort.
        }
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
        var objectKey = await users.GetAvatarObjectKeyAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(objectKey))
            return null;

        var metadata = await objects.InspectAsync(objectKey, ct);
        if (metadata is null)
            return null;

        return new(await objects.OpenReadAsync(objectKey, ct), metadata.ContentType);
    }
}
