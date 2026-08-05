using System.Buffers.Binary;
using NoCTF.Application.Authentication.Account;
using SkiaSharp;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SkiaAvatarImageProcessor : IAvatarImageProcessor
{
    private static readonly SKSamplingOptions Sampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public AvatarImageProcessingResult Process(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
            return AvatarImageProcessingResult.Rejected(AvatarImageFailure.SizeInvalid);

        try
        {
            using var data = SKData.CreateCopy(content.ToArray());
            using var metadataCodec = SKCodec.Create(data);
            if (metadataCodec is null)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);

            var sourceContentType = SourceContentType(metadataCodec.EncodedFormat);
            if (sourceContentType is null)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.UnsupportedFormat);

            var info = metadataCodec.Info;
            if (info.Width <= 0 || info.Height <= 0
                || info.Width > UserProfileRules.MaximumAvatarDimension
                || info.Height > UserProfileRules.MaximumAvatarDimension)
            {
                return AvatarImageProcessingResult.Rejected(
                    AvatarImageFailure.InvalidDimensions);
            }

            if ((long)info.Width * info.Height > UserProfileRules.MaximumAvatarPixels)
            {
                return AvatarImageProcessingResult.Rejected(
                    AvatarImageFailure.PixelLimitExceeded);
            }

            if (metadataCodec.FrameCount > 1
                || HasAnimationPayload(content.Span, metadataCodec.EncodedFormat))
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MultipleFrames);

            using var decodeCodec = SKCodec.Create(data);
            if (decodeCodec is null)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);

            var decodedInfo = new SKImageInfo(
                info.Width,
                info.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var decoded = new SKBitmap(decodedInfo);
            var decodeResult = decodeCodec.GetPixels(decodedInfo, decoded.GetPixels());
            if (decodeResult != SKCodecResult.Success)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);

            using var output = new SKBitmap(new SKImageInfo(
                UserProfileRules.AvatarOutputSize,
                UserProfileRules.AvatarOutputSize,
                SKColorType.Rgba8888,
                SKAlphaType.Premul));
            using (var canvas = new SKCanvas(output))
            {
                canvas.Clear(SKColors.Transparent);
                var sourceSide = Math.Min(decoded.Width, decoded.Height);
                var sourceLeft = (decoded.Width - sourceSide) / 2F;
                var sourceTop = (decoded.Height - sourceSide) / 2F;
                canvas.DrawBitmap(
                    decoded,
                    new SKRect(
                        sourceLeft,
                        sourceTop,
                        sourceLeft + sourceSide,
                        sourceTop + sourceSide),
                    new SKRect(
                        0,
                        0,
                        UserProfileRules.AvatarOutputSize,
                        UserProfileRules.AvatarOutputSize),
                    Sampling,
                    null);
                canvas.Flush();
            }

            using var encoded = output.Encode(SKEncodedImageFormat.Webp, 90);
            if (encoded is null || encoded.Size == 0)
                return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);

            return AvatarImageProcessingResult.Success(new(
                encoded.ToArray(),
                "image/webp",
                "webp",
                sourceContentType));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);
        }
    }

    private static string? SourceContentType(SKEncodedImageFormat format) =>
        format switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp",
            _ => null
        };

    private static bool HasAnimationPayload(
        ReadOnlySpan<byte> content,
        SKEncodedImageFormat format) =>
        format switch
        {
            SKEncodedImageFormat.Png => HasPngAnimationControl(content),
            SKEncodedImageFormat.Webp => HasWebpAnimationChunk(content),
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
