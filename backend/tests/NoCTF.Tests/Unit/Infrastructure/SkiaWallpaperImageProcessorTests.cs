using NoCTF.Application.Authentication.Account;
using NoCTF.Infrastructure.Authentication;
using SkiaSharp;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class SkiaWallpaperImageProcessorTests
{
    [Test]
    public async Task Supported_wallpaper_preserves_its_aspect_ratio_and_is_normalized()
    {
        using var sourceImage = new SKBitmap(640, 360);
        sourceImage.Erase(SKColors.CornflowerBlue);
        using var source = sourceImage.Encode(SKEncodedImageFormat.Png, 100);

        var result = new SkiaWallpaperImageProcessor().Process(source.ToArray());

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Image).IsNotNull();
        await Assert.That(result.Image!.ContentType).IsEqualTo("image/webp");
        await Assert.That(result.Image.SourceContentType).IsEqualTo("image/png");
        using var data = new MemoryStream(result.Image.Content.ToArray());
        using var metadata = SKCodec.Create(data);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.EncodedFormat).IsEqualTo(SKEncodedImageFormat.Webp);
        await Assert.That(metadata.Info.Width).IsEqualTo(640);
        await Assert.That(metadata.Info.Height).IsEqualTo(360);
    }
}
