using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Storage;
using NSubstitute;
using System.Security.Cryptography;

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
        var objects = Substitute.For<IObjectStorage>();
        var configuration = new ManagePlatformConfiguration(
            store,
            objects,
            Uploads(objects));

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
    public async Task Logo_honors_the_configured_byte_limit_before_storage()
    {
        var objects = Substitute.For<IObjectStorage>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var configuration = new ManagePlatformConfiguration(
            Substitute.For<IPlatformConfigurationStore>(),
            objects,
            new ManagedFileUploads(registry, objects));

        var result = await configuration.ReplaceLogoAsync(
            "logo.png",
            "image/png",
            new byte[] { 1, 2 },
            maximumBytes: 1,
            expectedRevision: 1,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.InvalidSize);
        await objects.DidNotReceiveWithAnyArgs().PutAsync(
            default!,
            default!,
            default!,
            default!,
            default);
    }

    [Test]
    public async Task Logo_replacement_rejects_mismatched_content_without_storage_write()
    {
        var objects = Substitute.For<IObjectStorage>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var configuration = new ManagePlatformConfiguration(
            Substitute.For<IPlatformConfigurationStore>(),
            objects,
            new ManagedFileUploads(registry, objects));

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
    public async Task Successful_logo_replacement_uses_the_new_file_reference()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformConfigurationStore>();
        var objects = Substitute.For<IObjectStorage>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Guid registeredFileId = default;
        Guid attachedFileId = default;
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
                Convert.ToHexString(SHA256.HashData(png))));
        registry.RegisterAsync(
                Arg.Do<Guid>(value => registeredFileId = value),
                Arg.Any<StoredObject>(),
                now,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        store.ReplaceLogoAsync(
                Arg.Do<Guid>(value => attachedFileId = value),
                2,
                now,
                Arg.Any<CancellationToken>())
            .Returns(call => new PlatformLogoReplacement(
                new PlatformConfigurationView(
                    "NoCTF",
                    null,
                    Guid.NewGuid(),
                    3,
                    now),
                Guid.NewGuid()));
        var configuration = new ManagePlatformConfiguration(
            store,
            objects,
            new ManagedFileUploads(registry, objects));

        var result = await configuration.ReplaceLogoAsync(
            "logo.png",
            "image/png",
            png,
            2,
            now);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.Updated);
        await Assert.That(registeredFileId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attachedFileId).IsEqualTo(registeredFileId);
        await registry.DidNotReceiveWithAnyArgs().AbandonAsync(default, default);
    }

    private static ManagedFileUploads Uploads(IObjectStorage objects) =>
        new(Substitute.For<IManagedFileUploadRegistry>(), objects);
}
