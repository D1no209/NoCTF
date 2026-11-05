using System.Buffers.Binary;
using NoCTF.Application.Authentication.Account;
using NoCTF.Infrastructure.Authentication;
using SkiaSharp;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class ImageProcessingSecurityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(5_000)]
    public async Task Tiff_is_rejected_before_identification_and_decoding(
        bool bigTiff,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = new byte[24];
        content[0] = content[1] = 0x49;
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(2), bigTiff ? (ushort)43 : (ushort)42);
        if (bigTiff)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt64LittleEndian(content.AsSpan(8), 16);
            BinaryPrimitives.WriteUInt64LittleEndian(content.AsSpan(16), 1_000);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(8), 1_000);
        }

        var avatar = new SkiaAvatarImageProcessor().Process(content);
        var wallpaper = new SkiaWallpaperImageProcessor().Process(content);

        await Assert.That(avatar.Failure).IsEqualTo(AvatarImageFailure.UnsupportedFormat);
        await Assert.That(wallpaper.Failure).IsEqualTo(WallpaperImageFailure.UnsupportedFormat);
    }

    [Test]
    public async Task Avatar_does_not_preserve_an_embedded_color_profile()
    {
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var sourceImage = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        sourceImage.Erase(SKColors.CornflowerBlue);
        using var source = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        var content = source.ToArray();
        using var originalData = new MemoryStream(content);
        using var original = SKCodec.Create(originalData);
        await Assert.That(original.Info.ColorSpace.GammaIsLinear).IsTrue();

        var result = new SkiaAvatarImageProcessor().Process(content);

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Image).IsNotNull();
        using var normalizedData = new MemoryStream(result.Image!.Content.ToArray());
        using var normalized = SKCodec.Create(normalizedData);
        await Assert.That(normalized.Info.ColorSpace?.GammaIsLinear == true).IsFalse();
    }
}
