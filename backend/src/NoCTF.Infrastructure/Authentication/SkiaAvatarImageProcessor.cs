using NoCTF.Application.Authentication.Account;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SkiaAvatarImageProcessor : IAvatarImageProcessor
{
    public AvatarImageProcessingResult Process(ReadOnlyMemory<byte> content)
    {
        var result = SkiaProfileImage.Process(content, wallpaper: false);
        return result.Failure is { } failure
            ? AvatarImageProcessingResult.Rejected(failure switch
            {
                ProfileImageFailure.SizeInvalid => AvatarImageFailure.SizeInvalid,
                ProfileImageFailure.UnsupportedFormat => AvatarImageFailure.UnsupportedFormat,
                ProfileImageFailure.InvalidDimensions => AvatarImageFailure.InvalidDimensions,
                ProfileImageFailure.PixelLimitExceeded => AvatarImageFailure.PixelLimitExceeded,
                ProfileImageFailure.MultipleFrames => AvatarImageFailure.MultipleFrames,
                ProfileImageFailure.MalformedImage => AvatarImageFailure.MalformedImage,
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            })
            : AvatarImageProcessingResult.Success(new(result.Content!, "image/webp", "webp", result.SourceContentType!));
    }
}
