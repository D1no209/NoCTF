using NoCTF.Application.Authentication.Account;
using NoCTF.Infrastructure.Authentication;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class ImageSharpWallpaperImageProcessorTests
{
    [Test]
    public async Task Supported_wallpaper_preserves_its_aspect_ratio_and_is_normalized()
    {
        using var sourceImage = new Image<Rgba32>(640, 360, Color.CornflowerBlue);
        using var source = new MemoryStream();
        sourceImage.Save(source, new PngEncoder());

        var result = new ImageSharpWallpaperImageProcessor().Process(source.ToArray());

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Image).IsNotNull();
        await Assert.That(result.Image!.ContentType).IsEqualTo("image/webp");
        await Assert.That(result.Image.SourceContentType).IsEqualTo("image/png");
        var metadata = Image.Identify(result.Image.Content.Span);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.Metadata.DecodedImageFormat).IsTypeOf<WebpFormat>();
        await Assert.That(metadata.Width).IsEqualTo(640);
        await Assert.That(metadata.Height).IsEqualTo(360);
    }
}
