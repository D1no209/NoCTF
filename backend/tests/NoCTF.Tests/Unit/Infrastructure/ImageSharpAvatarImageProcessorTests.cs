using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using NoCTF.Application.Authentication.Account;
using NoCTF.Infrastructure.Authentication;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class ImageSharpAvatarImageProcessorTests
{
    private readonly ImageSharpAvatarImageProcessor processor = new();

    [Test]
    [Arguments(AvatarTestFormat.Jpeg, "image/jpeg")]
    [Arguments(AvatarTestFormat.Png, "image/png")]
    [Arguments(AvatarTestFormat.Webp, "image/webp")]
    public async Task Supported_images_are_normalized_to_square_webp(
        AvatarTestFormat format,
        string expectedSourceContentType)
    {
        var source = CreateEncodedImage(640, 480, format);

        var result = processor.Process(source);

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Image).IsNotNull();
        await Assert.That(result.Image!.ContentType).IsEqualTo("image/webp");
        await Assert.That(result.Image.Extension).IsEqualTo("webp");
        await Assert.That(result.Image.SourceContentType).IsEqualTo(expectedSourceContentType);

        var metadata = Image.Identify(result.Image.Content.Span);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.Metadata.DecodedImageFormat).IsTypeOf<WebpFormat>();
        await Assert.That(metadata.Width).IsEqualTo(UserProfileRules.AvatarOutputSize);
        await Assert.That(metadata.Height).IsEqualTo(UserProfileRules.AvatarOutputSize);

        using var decoded = Image.Load<Rgba32>(result.Image.Content.Span);
        await Assert.That(decoded.Frames.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Truncated_image_is_rejected()
    {
        var source = CreateEncodedImage(64, 64, AvatarTestFormat.Png);

        var result = processor.Process(source.AsMemory(0, source.Length / 2));

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.MalformedImage);
    }

    [Test]
    public async Task Landscape_image_is_center_cropped_before_resizing()
    {
        using var sourceImage = new Image<Rgba32>(6, 2, new Rgba32(255, 0, 0));
        for (var y = 0; y < sourceImage.Height; y++)
        {
            sourceImage[2, y] = new Rgba32(0, 0, 255);
            sourceImage[3, y] = new Rgba32(0, 0, 255);
        }

        using var source = new MemoryStream();
        sourceImage.Save(source, new PngEncoder());

        var result = processor.Process(source.ToArray());

        await Assert.That(result.Failure).IsNull();
        using var output = Image.Load<Rgba32>(result.Image!.Content.Span);
        var center = output[output.Width / 2, output.Height / 2];
        await Assert.That(center.B).IsGreaterThan(center.R);
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
        AvatarTestFormat format)
    {
        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        using var output = new MemoryStream();
        switch (format)
        {
            case AvatarTestFormat.Jpeg:
                image.Save(output, new JpegEncoder { Quality = 90 });
                break;
            case AvatarTestFormat.Png:
                image.Save(output, new PngEncoder());
                break;
            case AvatarTestFormat.Webp:
                image.Save(output, new WebpEncoder { Quality = 90 });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }

        return output.ToArray();
    }

    private static byte[] CreatePngWithDimensions(int width, int height)
    {
        var png = CreateEncodedImage(1, 1, AvatarTestFormat.Png);
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

    public enum AvatarTestFormat
    {
        Jpeg,
        Png,
        Webp
    }
}
