using System.Buffers.Binary;
using NoCTF.Application.Authentication.Account;
using NoCTF.Infrastructure.Authentication;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

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

        var avatar = new ImageSharpAvatarImageProcessor().Process(content);
        var wallpaper = new ImageSharpWallpaperImageProcessor().Process(content);

        await Assert.That(avatar.Failure).IsEqualTo(AvatarImageFailure.UnsupportedFormat);
        await Assert.That(wallpaper.Failure).IsEqualTo(WallpaperImageFailure.UnsupportedFormat);
    }

    [Test]
    public async Task Avatar_does_not_preserve_an_embedded_color_profile()
    {
        var profile = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        "acsp"u8.CopyTo(profile.AsSpan(36));
        using var sourceImage = new Image<Rgba32>(32, 32, Color.CornflowerBlue);
        sourceImage.Metadata.IccProfile = new IccProfile(profile);
        using var source = new MemoryStream();
        sourceImage.Save(source, new PngEncoder());
        var content = source.ToArray();
        using var original = Image.Load<Rgba32>(content);
        await Assert.That(original.Metadata.IccProfile).IsNotNull();

        var result = new ImageSharpAvatarImageProcessor().Process(content);

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Image).IsNotNull();
        using var normalized = Image.Load<Rgba32>(result.Image!.Content.Span);
        await Assert.That(normalized.Metadata.IccProfile).IsNull();
    }
}
