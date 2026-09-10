using System.Buffers.Binary;
using NoCTF.Application.Authentication.Account;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoCTF.Infrastructure.Authentication;

public sealed class ImageSharpWallpaperImageProcessor : IWallpaperImageProcessor
{
    public WallpaperImageProcessingResult Process(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
            return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.SizeInvalid);

        try
        {
            var sourceFormat = Image.DetectFormat(content.Span);
            var sourceContentType = SourceContentType(sourceFormat);
            if (sourceContentType is null)
                return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.UnsupportedFormat);

            if (HasAnimationPayload(content.Span, sourceFormat))
                return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.MultipleFrames);

            var metadata = Image.Identify(content.Span);
            if (metadata is null)
                return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.MalformedImage);
            if (metadata.Width <= 0 || metadata.Height <= 0
                || metadata.Width > UserWallpaperRules.MaximumSourceDimension
                || metadata.Height > UserWallpaperRules.MaximumSourceDimension)
            {
                return WallpaperImageProcessingResult.Rejected(
                    WallpaperImageFailure.InvalidDimensions);
            }
            if ((long)metadata.Width * metadata.Height > UserWallpaperRules.MaximumSourcePixels)
                return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.PixelLimitExceeded);

            using var image = Image.Load<Rgba32>(content.Span);
            if (image.Frames.Count > 1)
                return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.MultipleFrames);

            image.Mutate(context => context.AutoOrient());
            var longestSide = Math.Max(image.Width, image.Height);
            if (longestSide > UserWallpaperRules.MaximumOutputDimension)
            {
                var scale = (double)UserWallpaperRules.MaximumOutputDimension / longestSide;
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IccProfile = null;
            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder { Quality = 88 });
            if (output.Length == 0)
                return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.MalformedImage);

            return WallpaperImageProcessingResult.Success(new(
                output.ToArray(),
                "image/webp",
                "webp",
                sourceContentType));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.MalformedImage);
        }
    }

    private static string? SourceContentType(IImageFormat? format) =>
        format switch
        {
            JpegFormat => "image/jpeg",
            PngFormat => "image/png",
            WebpFormat => "image/webp",
            _ => null
        };

    private static bool HasAnimationPayload(
        ReadOnlySpan<byte> content,
        IImageFormat? format) =>
        format switch
        {
            PngFormat => HasPngAnimationControl(content),
            WebpFormat => HasWebpAnimationChunk(content),
            _ => false
        };

    private static bool HasPngAnimationControl(ReadOnlySpan<byte> content)
    {
        const int signatureLength = 8;
        const int chunkHeaderLength = 8;
        const int chunkCrcLength = 4;
        if (content.Length < signatureLength)
            return false;

        var offset = signatureLength;
        while (offset <= content.Length - chunkHeaderLength)
        {
            var dataLength = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset, 4));
            var chunkLength = (long)chunkHeaderLength + dataLength + chunkCrcLength;
            if (chunkLength > content.Length - offset)
                return false;

            var chunkType = content.Slice(offset + 4, 4);
            if (chunkType.SequenceEqual("acTL"u8))
            {
                return dataLength < sizeof(uint)
                    || BinaryPrimitives.ReadUInt32BigEndian(
                        content.Slice(offset + chunkHeaderLength, sizeof(uint))) > 1;
            }
            if (chunkType.SequenceEqual("IEND"u8))
                return false;
            offset += (int)chunkLength;
        }

        return false;
    }

    private static bool HasWebpAnimationChunk(ReadOnlySpan<byte> content)
    {
        const int riffHeaderLength = 12;
        const int chunkHeaderLength = 8;
        if (content.Length < riffHeaderLength
            || !content[..4].SequenceEqual("RIFF"u8)
            || !content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var offset = riffHeaderLength;
        while (offset <= content.Length - chunkHeaderLength)
        {
            var chunkType = content.Slice(offset, 4);
            var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(
                content.Slice(offset + 4, 4));
            var paddedDataLength = (long)dataLength + (dataLength & 1);
            var chunkLength = chunkHeaderLength + paddedDataLength;
            if (chunkLength > content.Length - offset)
                return false;
            if (chunkType.SequenceEqual("ANIM"u8) || chunkType.SequenceEqual("ANMF"u8))
                return true;
            offset += (int)chunkLength;
        }

        return false;
    }
}
