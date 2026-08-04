using NoCTF.Application.Common;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Authentication.Account;

public static class UserProfileRules
{
    public const int MaximumDescriptionLength = 500;
    public const int MaximumAvatarBytes = 3 * 1024 * 1024;
}

public sealed class UpdateCurrentUserProfile(IUserAuthenticationStore users)
{
    public Task<UserProfile?> ExecuteAsync(
        Guid userId,
        string? description,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        return users.UpdateProfileAsync(userId, normalizedDescription, now, ct);
    }
}

public sealed class ReplaceCurrentUserAvatar(
    IUserAuthenticationStore users,
    IObjectStorage objects)
{
    public async Task<OperationResult<UserProfile>> ExecuteAsync(
        Guid userId,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (content.IsEmpty || content.Length > UserProfileRules.MaximumAvatarBytes)
            return OperationResult<UserProfile>.Failure(
                "avatar_size_invalid",
                $"Avatar must be between 1 byte and {UserProfileRules.MaximumAvatarBytes} bytes.");

        var image = DetectImage(content.Span);
        if (image is null || !string.Equals(contentType, image.Value.ContentType, StringComparison.OrdinalIgnoreCase))
            return OperationResult<UserProfile>.Failure(
                "avatar_format_invalid",
                "Avatar must be a JPEG, PNG, or WebP image with matching content type.");

        var objectKey = $"users/{userId:N}/avatars/{Guid.CreateVersion7(now):N}.{image.Value.Extension}";
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
            return OperationResult<UserProfile>.Failure("user_not_found", "User was not found.");
        }

        if (!string.IsNullOrWhiteSpace(replacement.PreviousObjectKey)
            && !string.Equals(replacement.PreviousObjectKey, stored.ObjectKey, StringComparison.Ordinal))
            await TryDeleteAsync(replacement.PreviousObjectKey);

        return OperationResult<UserProfile>.Success(replacement.Profile);
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
