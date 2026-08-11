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

public sealed class ImageSharpAvatarImageProcessor : IAvatarImageProcessor
{
    public AvatarImageProcessingResult Process(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
            return AvatarImageProcessingResult.Rejected(AvatarImageFailure.SizeInvalid);

        try
        {
            var sourceFormat = Image.DetectFormat(content.Span);
            var sourceContentType = SourceContentType(sourceFormat);
            if (sourceContentType is null)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.UnsupportedFormat);

            if (HasAnimationPayload(content.Span, sourceFormat))
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MultipleFrames);

            var metadata = Image.Identify(content.Span);
            if (metadata is null)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);

            if (metadata.Width <= 0 || metadata.Height <= 0
                || metadata.Width > UserProfileRules.MaximumAvatarDimension
                || metadata.Height > UserProfileRules.MaximumAvatarDimension)
            {
                return AvatarImageProcessingResult.Rejected(
                    AvatarImageFailure.InvalidDimensions);
            }

            if ((long)metadata.Width * metadata.Height > UserProfileRules.MaximumAvatarPixels)
            {
                return AvatarImageProcessingResult.Rejected(
                    AvatarImageFailure.PixelLimitExceeded);
            }

            using var image = Image.Load<Rgba32>(content.Span);
            if (image.Frames.Count > 1)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MultipleFrames);

            var sourceSide = Math.Min(image.Width, image.Height);
            var sourceLeft = (image.Width - sourceSide) / 2;
            var sourceTop = (image.Height - sourceSide) / 2;
            image.Mutate(context => context
                .Crop(new Rectangle(sourceLeft, sourceTop, sourceSide, sourceSide))
                .Resize(new ResizeOptions
                {
                    Size = new Size(
                        UserProfileRules.AvatarOutputSize,
                        UserProfileRules.AvatarOutputSize),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Triangle
                }));

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder { Quality = 90 });
            if (output.Length == 0)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);

            return AvatarImageProcessingResult.Success(new(
                output.ToArray(),
                "image/webp",
                "webp",
                sourceContentType));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);
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

            if (chunkType.SequenceEqual("ANIM"u8)
                || chunkType.SequenceEqual("ANMF"u8))
            {
                return true;
            }

            offset += (int)chunkLength;
        }

        return false;
    }
}
