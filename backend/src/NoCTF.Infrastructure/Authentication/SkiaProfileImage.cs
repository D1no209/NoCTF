using NoCTF.Application.Authentication.Account;
using SkiaSharp;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.IO;
using MetadataExtractor.Util;

namespace NoCTF.Infrastructure.Authentication;

internal enum ProfileImageFailure { SizeInvalid, UnsupportedFormat, InvalidDimensions, PixelLimitExceeded, MultipleFrames, MalformedImage }
internal sealed record ProfileImageResult(byte[]? Content = null, string? SourceContentType = null, ProfileImageFailure? Failure = null);

internal static class SkiaProfileImage
{
    internal static ProfileImageResult Process(ReadOnlyMemory<byte> content, bool wallpaper)
    {
        if (content.IsEmpty) return new(Failure: ProfileImageFailure.SizeInvalid);
        var bytes = content.ToArray();
        using var input = new MemoryStream(bytes, writable: false);
        var type = FileTypeDetector.DetectFileType(input);
        if (type is not (FileType.Jpeg or FileType.Png or FileType.WebP))
            return new(Failure: type == FileType.Unknown ? ProfileImageFailure.MalformedImage : ProfileImageFailure.UnsupportedFormat);
        if (type == FileType.Png)
        {
            try
            {
                // 完整区块读取器识别 PNG 动画，避免解码器只返回默认帧。
                if (new PngChunkReader().Extract(new BoundedMetadataReader(bytes), [new PngChunkType("acTL")]).Any())
                    return new(Failure: ProfileImageFailure.MultipleFrames);
            }
            catch (Exception error) when (error is PngProcessingException or IOException or ArgumentException)
            {
                return new(Failure: ProfileImageFailure.MalformedImage);
            }
        }
        using var codec = SKCodec.Create(input, out var created);
        if (codec is null) return new(Failure: created == SKCodecResult.Unimplemented
            ? ProfileImageFailure.UnsupportedFormat : ProfileImageFailure.MalformedImage);
        var sourceContentType = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp",
            _ => null
        };
        if (sourceContentType is null) return new(Failure: ProfileImageFailure.UnsupportedFormat);
        var info = codec.Info;
        var maximumDimension = wallpaper ? UserWallpaperRules.MaximumSourceDimension : UserProfileRules.MaximumAvatarDimension;
        var maximumPixels = wallpaper ? UserWallpaperRules.MaximumSourcePixels : UserProfileRules.MaximumAvatarPixels;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > maximumDimension || info.Height > maximumDimension)
            return new(Failure: ProfileImageFailure.InvalidDimensions);
        if ((long)info.Width * info.Height > maximumPixels) return new(Failure: ProfileImageFailure.PixelLimitExceeded);
        if (codec.FrameCount > 1) return new(Failure: ProfileImageFailure.MultipleFrames);

        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            return new(Failure: ProfileImageFailure.MalformedImage);
        using var image = SKImage.FromBitmap(decoded);
        var origin = wallpaper ? codec.EncodedOrigin : SKEncodedOrigin.TopLeft;
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapsAxes ? info.Height : info.Width;
        var height = swapsAxes ? info.Width : info.Height;
        var scale = Math.Min(1d, (double)UserWallpaperRules.MaximumOutputDimension / Math.Max(width, height));
        var outputWidth = wallpaper ? Math.Max(1, (int)Math.Round(width * scale)) : UserProfileRules.AvatarOutputSize;
        var outputHeight = wallpaper ? Math.Max(1, (int)Math.Round(height * scale)) : UserProfileRules.AvatarOutputSize;
        using var output = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            if (wallpaper)
            {
                canvas.Scale((float)outputWidth / width, (float)outputHeight / height);
                canvas.Concat(Orientation(origin, info.Width, info.Height));
                canvas.DrawImage(image, 0, 0, sampling);
            }
            else
            {
                var side = Math.Min(width, height);
                var left = (width - side) / 2f;
                var top = (height - side) / 2f;
                canvas.DrawImage(image, new SKRect(left, top, left + side, top + side),
                    new SKRect(0, 0, outputWidth, outputHeight), sampling);
            }
        }
        using var normalized = SKImage.FromBitmap(output);
        using var encoded = normalized.Encode(SKEncodedImageFormat.Webp, wallpaper ? 88 : 90);
        if (encoded is null || encoded.Size == 0) throw new InvalidOperationException("WebP encoding failed.");
        return new(encoded.ToArray(), sourceContentType);
    }

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopLeft => SKMatrix.Identity,
        SKEncodedOrigin.TopRight => new(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };

    private sealed class BoundedMetadataReader(byte[] bytes) : SequentialByteArrayReader(bytes)
    {
        public override byte[] GetBytes(int count)
        {
            if (count < 0 || count > Available()) throw new IOException("Image metadata extends past the input.");
            return base.GetBytes(count);
        }
    }
}
