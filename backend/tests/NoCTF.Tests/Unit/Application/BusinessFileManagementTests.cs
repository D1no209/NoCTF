using System.Security.Cryptography;
using NSubstitute;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class BusinessFileManagementTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-08T00:00:00Z");

    [Test]
    public async Task Team_avatar_attaches_the_registered_file_without_abandonment()
    {
        var references = Substitute.For<IBusinessFileReferenceStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var content = new byte[] { 1, 2, 3, 4 };
        var objects = ObjectStorageFor(content);
        Guid registeredFileId = default;
        Guid attachedFileId = default;
        registry.RegisterAsync(
                Arg.Do<Guid>(value => registeredFileId = value),
                Arg.Any<StoredObject>(),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        references.ReplaceTeamAvatarAsync(
                Arg.Any<Guid>(),
                false,
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Do<Guid>(value => attachedFileId = value),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(new BusinessFileReferenceResult(BusinessFileReferenceState.Updated));
        var images = new ManageBusinessImages(
            references,
            objects,
            new ManagedFileUploads(registry, objects));

        var result = await images.ReplaceTeamAvatarAsync(
            Guid.NewGuid(),
            false,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "avatar.png",
            "image/png",
            new MemoryStream(content),
            Now);

        await Assert.That(result.State).IsEqualTo(BusinessFileReferenceState.Updated);
        await Assert.That(registeredFileId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attachedFileId).IsEqualTo(registeredFileId);
        await registry.DidNotReceiveWithAnyArgs().AbandonAsync(default, default);
    }

    [Test]
    public async Task Rejected_competition_poster_abandons_the_registered_file()
    {
        var references = Substitute.For<IBusinessFileReferenceStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var content = new byte[] { 9, 8, 7 };
        var objects = ObjectStorageFor(content);
        Guid registeredFileId = default;
        registry.RegisterAsync(
                Arg.Do<Guid>(value => registeredFileId = value),
                Arg.Any<StoredObject>(),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        references.ReplaceCompetitionPosterAsync(
                Arg.Any<Guid>(),
                false,
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(new BusinessFileReferenceResult(BusinessFileReferenceState.Forbidden));
        var images = new ManageBusinessImages(
            references,
            objects,
            new ManagedFileUploads(registry, objects));

        var result = await images.ReplaceCompetitionPosterAsync(
            Guid.NewGuid(),
            false,
            Guid.NewGuid(),
            "poster.png",
            "image/png",
            new MemoryStream(content),
            Now);

        await Assert.That(result.State).IsEqualTo(BusinessFileReferenceState.Forbidden);
        await Assert.That(registeredFileId).IsNotEqualTo(Guid.Empty);
        await registry.Received(1).AbandonAsync(registeredFileId, CancellationToken.None);
    }

    private static IObjectStorage ObjectStorageFor(byte[] content)
    {
        var objects = Substitute.For<IObjectStorage>();
        objects.PutAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new StoredObject(
                call.ArgAt<string>(0),
                call.ArgAt<string>(1),
                call.ArgAt<string>(2),
                content.LongLength,
                Convert.ToHexString(SHA256.HashData(content))));
        return objects;
    }
}
