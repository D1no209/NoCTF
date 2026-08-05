using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using NoCTF.Application.Authentication.Account;
using NoCTF.Infrastructure.Authentication;
using SkiaSharp;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class SkiaAvatarImageProcessorTests
{
    private readonly SkiaAvatarImageProcessor processor = new();

    [Test]
    [Arguments(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [Arguments(SKEncodedImageFormat.Png, "image/png")]
    [Arguments(SKEncodedImageFormat.Webp, "image/webp")]
    public async Task Supported_images_are_normalized_to_square_webp(
        SKEncodedImageFormat format,
        string expectedSourceContentType)
    {
        var source = CreateEncodedImage(640, 480, format);

        var result = processor.Process(source);

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Image).IsNotNull();
        await Assert.That(result.Image!.ContentType).IsEqualTo("image/webp");
        await Assert.That(result.Image.Extension).IsEqualTo("webp");
        await Assert.That(result.Image.SourceContentType).IsEqualTo(expectedSourceContentType);

        using var data = SKData.CreateCopy(result.Image.Content.ToArray());
        using var codec = SKCodec.Create(data);
        await Assert.That(codec).IsNotNull();
        await Assert.That(codec!.EncodedFormat).IsEqualTo(SKEncodedImageFormat.Webp);
        await Assert.That(codec.Info.Width).IsEqualTo(UserProfileRules.AvatarOutputSize);
        await Assert.That(codec.Info.Height).IsEqualTo(UserProfileRules.AvatarOutputSize);
        await Assert.That(codec.FrameCount).IsLessThanOrEqualTo(1);
    }

    [Test]
    public async Task Truncated_image_is_rejected()
    {
        var source = CreateEncodedImage(64, 64, SKEncodedImageFormat.Png);

        var result = processor.Process(source.AsMemory(0, source.Length / 2));

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.MalformedImage);
    }

    [Test]
    public async Task Unsupported_image_format_is_rejected()
    {
        var gif = Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

        var result = processor.Process(gif);

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.UnsupportedFormat);
    }

    [Test]
    public async Task Dimension_limit_is_checked_before_pixel_decode()
    {
        var source = CreatePngWithDimensions(UserProfileRules.MaximumAvatarDimension + 1, 1);

        var result = processor.Process(source);

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.InvalidDimensions);
    }

    [Test]
    public async Task Pixel_limit_is_checked_before_pixel_decode()
    {
        var source = CreatePngWithDimensions(8_000, 4_001);

        var result = processor.Process(source);

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.PixelLimitExceeded);
    }

    [Test]
    public async Task Animated_image_is_rejected()
    {
        var result = processor.Process(CreateAnimatedPng());

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.MultipleFrames);
    }

    private static byte[] CreateEncodedImage(
        int width,
        int height,
        SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            canvas.Flush();
        }

        using var encoded = bitmap.Encode(format, 90);
        return encoded.ToArray();
    }

    private static byte[] CreatePngWithDimensions(int width, int height)
    {
        var png = CreateEncodedImage(1, 1, SKEncodedImageFormat.Png);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), height);
        var checksum = Crc32.HashToUInt32(png.AsSpan(12, 17));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29, 4), checksum);
        return png;
    }

    private static byte[] CreateAnimatedPng()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], 1);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(stream, "IHDR"u8, header);

        Span<byte> animationControl = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(animationControl[..4], 2);
        WritePngChunk(stream, "acTL"u8, animationControl);

        WritePngChunk(stream, "fcTL"u8, CreateFrameControl(0));
        WritePngChunk(stream, "IDAT"u8, CompressScanline(255, 0, 0));
        WritePngChunk(stream, "fcTL"u8, CreateFrameControl(1));

        var secondFrame = CompressScanline(0, 0, 255);
        var frameData = new byte[secondFrame.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frameData.AsSpan(0, 4), 2);
        secondFrame.CopyTo(frameData, 4);
        WritePngChunk(stream, "fdAT"u8, frameData);
        WritePngChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    private static byte[] CreateFrameControl(int sequenceNumber)
    {
        var control = new byte[26];
        BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(0, 4), sequenceNumber);
        BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22, 2), 10);
        return control;
    }

    private static byte[] CompressScanline(byte red, byte green, byte blue)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            zlib.Write(new byte[] { 0, red, green, blue, 255 });
        return compressed.ToArray();
    }

    private static void WritePngChunk(
        Stream stream,
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);

        var checksumData = new byte[type.Length + data.Length];
        type.CopyTo(checksumData);
        data.CopyTo(checksumData.AsSpan(type.Length));
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, Crc32.HashToUInt32(checksumData));
        stream.Write(checksum);
    }
}
