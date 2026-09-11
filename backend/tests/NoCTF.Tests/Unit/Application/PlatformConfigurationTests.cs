using FluentStorage.Storage;
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
                now,
                Arg.Any<CancellationToken>())
            .Returns(new PlatformConfigurationView(
                "NoCTF Arena",
                "Competition platform",
                null,
                true,
                now));
        var objects = Substitute.For<IStore>();
        var configuration = new ManagePlatformConfiguration(
            store,
            objects,
            Uploads(objects));

        var result = await configuration.UpdateAsync(
            "  NoCTF Arena  ",
            "  Competition platform  ",
            now);

        await Assert.That(result.State).IsEqualTo(PlatformConfigurationUpdateState.Updated);
        await store.Received(1).UpdateAsync(
            "NoCTF Arena",
            "Competition platform",
            now,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Human_verification_switch_is_persisted_with_the_update_time()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformConfigurationStore>();
        store.UpdateHumanVerificationAsync(false, now, Arg.Any<CancellationToken>())
            .Returns(new PlatformConfigurationView("NoCTF", null, null, false, now));
        var objects = Substitute.For<IStore>();
        var configuration = new ManagePlatformConfiguration(store, objects, Uploads(objects));

        var result = await configuration.UpdateHumanVerificationAsync(false, now);

        await Assert.That(result.HumanVerificationEnabled).IsFalse();
        await store.Received(1).UpdateHumanVerificationAsync(
            false, now, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Logo_honors_the_configured_byte_limit_before_storage()
    {
        var objects = Substitute.For<IStore>();
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
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.InvalidSize);
        await objects.DidNotReceiveWithAnyArgs().SetObject(
            default!,
            default!,
            default!,
            default,
            default);
    }

    [Test]
    public async Task Logo_replacement_rejects_mismatched_content_without_storage_write()
    {
        var objects = Substitute.For<IStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var configuration = new ManagePlatformConfiguration(
            Substitute.For<IPlatformConfigurationStore>(),
            objects,
            new ManagedFileUploads(registry, objects));

        var result = await configuration.ReplaceLogoAsync(
            "logo.png",
            "image/png",
            new byte[] { 0xFF, 0xD8, 0xFF, 0x00 },
            16,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.InvalidFormat);
        await objects.DidNotReceiveWithAnyArgs().SetObject(
            default!,
            default!,
            default!,
            default,
            default);
    }

    [Test]
    public async Task Successful_logo_replacement_uses_the_new_file_reference()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformConfigurationStore>();
        var objects = Substitute.For<IStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Guid registeredFileId = default;
        Guid attachedFileId = default;
        objects.SetObject(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                "image/png",
                false,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        registry.RegisterAsync(
                Arg.Do<ManagedFileUpload>(value => registeredFileId = value.FileId),
                now,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        store.ReplaceLogoAsync(
                Arg.Do<Guid>(value => attachedFileId = value),
                now,
                Arg.Any<CancellationToken>())
            .Returns(call => new PlatformLogoReplacement(
                new PlatformConfigurationView(
                    "NoCTF",
                    null,
                    Guid.NewGuid(),
                    true,
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
            now);

        await Assert.That(result.State).IsEqualTo(PlatformLogoUpdateState.Updated);
        await Assert.That(registeredFileId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attachedFileId).IsEqualTo(registeredFileId);
        await registry.DidNotReceiveWithAnyArgs().AbandonAsync(default, default);
    }

    private static ManagedFileUploads Uploads(IStore objects) =>
        new(Substitute.For<IManagedFileUploadRegistry>(), objects);
}
