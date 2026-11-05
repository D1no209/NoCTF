using NoCTF.Application.Authentication.Account;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SkiaWallpaperImageProcessor : IWallpaperImageProcessor
{
    public WallpaperImageProcessingResult Process(ReadOnlyMemory<byte> content)
    {
        var result = SkiaProfileImage.Process(content, wallpaper: true);
        return result.Failure is { } failure
            ? WallpaperImageProcessingResult.Rejected(failure switch
            {
                ProfileImageFailure.SizeInvalid => WallpaperImageFailure.SizeInvalid,
                ProfileImageFailure.UnsupportedFormat => WallpaperImageFailure.UnsupportedFormat,
                ProfileImageFailure.InvalidDimensions => WallpaperImageFailure.InvalidDimensions,
                ProfileImageFailure.PixelLimitExceeded => WallpaperImageFailure.PixelLimitExceeded,
                ProfileImageFailure.MultipleFrames => WallpaperImageFailure.MultipleFrames,
                ProfileImageFailure.MalformedImage => WallpaperImageFailure.MalformedImage,
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            })
            : WallpaperImageProcessingResult.Success(new(result.Content!, "image/webp", "webp", result.SourceContentType!));
    }
}
