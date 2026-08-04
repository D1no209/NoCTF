using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Storage;
using NSubstitute;

namespace NoCTF.Tests.Unit.Application;

public sealed class PlatformConfigurationTests
{
    [Test]
    public async Task Update_normalizes_public_branding_text()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformConfigurationStore>();
        store.UpdateAsync(
                "NoCTF Arena",
                "Competition platform",
                3,
                now,
                Arg.Any<CancellationToken>())
            .Returns(new PlatformConfigurationView(
                "NoCTF Arena",
                "Competition platform",
                null,
                4,
                now));
        var configuration = new ManagePlatformConfiguration(
            store,
            Substitute.For<IObjectStorage>());

        var result = await configuration.UpdateAsync(
            "  NoCTF Arena  ",
            "  Competition platform  ",
            3,
            now);

        await Assert.That(result.State).IsEqualTo(PlatformConfigurationUpdateState.Updated);
        await store.Received(1).UpdateAsync(
            "NoCTF Arena",
            "Competition platform",
            3,
            now,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Logo_replacement_rejects_mismatched_content_without_storage_write()
    {
        var objects = Substitute.For<IObjectStorage>();
        var configuration = new ManagePlatformConfiguration(
            Substitute.For<IPlatformConfigurationStore>(),
            objects);

        var result = await configuration.ReplaceLogoAsync(
            "logo.png",
            "image/png",
            new byte[] { 0xFF, 0xD8, 0xFF, 0x00 },
            1,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.InvalidFormat);
        await objects.DidNotReceiveWithAnyArgs().PutAsync(
            default!,
            default!,
            default!,
            default!,
            default);
    }

    [Test]
    public async Task Successful_logo_replacement_cleans_the_previous_object()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformConfigurationStore>();
        var objects = Substitute.For<IObjectStorage>();
        objects.PutAsync(
                Arg.Any<string>(),
                "logo.png",
                "image/png",
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new StoredObject(
                call.ArgAt<string>(0),
                "logo.png",
                "image/png",
                8,
                "sha256"));
        store.ReplaceLogoAsync(
                Arg.Any<string>(),
                2,
                now,
                Arg.Any<CancellationToken>())
            .Returns(call => new PlatformLogoReplacement(
                new PlatformConfigurationView(
                    "NoCTF",
                    null,
                    call.ArgAt<string>(0),
                    3,
                    now),
                "platform/logo/previous.png"));
        var configuration = new ManagePlatformConfiguration(store, objects);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var result = await configuration.ReplaceLogoAsync(
            "logo.png",
            "image/png",
            png,
            2,
            now);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.Updated);
        await objects.Received(1).DeleteAsync(
            "platform/logo/previous.png",
            CancellationToken.None);
    }
}
