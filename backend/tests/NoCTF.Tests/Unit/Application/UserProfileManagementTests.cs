using FluentStorage.Storage;
using NSubstitute;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Storage;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.Application;

public sealed class UserProfileManagementTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-04T00:00:00Z");

    [Test]
    public async Task Description_is_trimmed_and_blank_text_is_cleared()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        users.UpdateProfileAsync(
                UserId,
                Arg.Any<string?>(),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Profile(description: "profile"));
        var update = new UpdateCurrentUserProfile(users);

        await update.ExecuteAsync(UserId, "  profile  ", Now);
        await update.ExecuteAsync(UserId, "   ", Now);

        await users.Received(1).UpdateProfileAsync(
            UserId,
            "profile",
            Now,
            Arg.Any<CancellationToken>());
        await users.Received(1).UpdateProfileAsync(
            UserId,
            null,
            Now,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Public_profile_never_projects_email()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        users.GetProfileAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Profile());

        var profile = await new GetPublicUserProfile(users).ExecuteAsync(UserId);

        await Assert.That(profile).IsNotNull();
        await Assert.That(profile!.UserName).IsEqualTo("Player");
    }

    [Test]
    public async Task Avatar_honors_the_configured_byte_limit_before_decoding()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var images = Substitute.For<IAvatarImageProcessor>();
        var replace = new ReplaceCurrentUserAvatar(
            users,
            new ManagedFileUploads(registry, objects),
            images);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 1, 2 },
            maximumBytes: 1,
            Now);

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.SizeInvalid);
        images.DidNotReceiveWithAnyArgs().Process(default);
        await objects.DidNotReceiveWithAnyArgs()
            .SetObject(default!, default!, default!, default, default);
    }

    [Test]
    public async Task Avatar_rejects_content_when_signature_and_content_type_do_not_match()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var images = Substitute.For<IAvatarImageProcessor>();
        images.Process(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage));
        var replace = new ReplaceCurrentUserAvatar(
            users,
            new ManagedFileUploads(registry, objects),
            images);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 1, 2, 3, 4 },
            Now);

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.MalformedImage);
        await objects.DidNotReceiveWithAnyArgs()
            .SetObject(default!, default!, default!, default, default);
    }

    [Test]
    public async Task Avatar_switches_to_the_new_file_reference()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var images = Substitute.For<IAvatarImageProcessor>();
        var currentFileId = Guid.NewGuid();
        var normalized = new byte[] { 9, 8, 7 };
        Guid registeredFileId = default;
        Guid attachedFileId = default;
        images.Process(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(AvatarImageProcessingResult.Success(new(
                normalized,
                "image/webp",
                "webp",
                "image/png")));
        objects.SetObject(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                "image/webp",
                false,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        registry.RegisterAsync(
                Arg.Do<ManagedFileUpload>(value => registeredFileId = value.FileId),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        users.ReplaceAvatarAsync(
                UserId,
                Arg.Do<Guid>(value => attachedFileId = value),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(new UserAvatarReplacement(Profile(avatarFileId: currentFileId), Guid.NewGuid()));
        var replace = new ReplaceCurrentUserAvatar(
            users,
            new ManagedFileUploads(registry, objects),
            images);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            Now);

        await Assert.That(result.Profile).IsNotNull();
        await Assert.That(registeredFileId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attachedFileId).IsEqualTo(registeredFileId);
        await users.Received(1).ReplaceAvatarAsync(
            UserId,
            Arg.Any<Guid>(),
            Now,
            Arg.Any<CancellationToken>());
        await registry.DidNotReceiveWithAnyArgs().AbandonAsync(default, default);
    }

    [Test]
    public async Task Avatar_rejects_forged_file_metadata_after_successful_decode()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IStore>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var images = Substitute.For<IAvatarImageProcessor>();
        images.Process(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(AvatarImageProcessingResult.Success(new(
                new byte[] { 9, 8, 7 },
                "image/webp",
                "webp",
                "image/png")));

        var result = await new ReplaceCurrentUserAvatar(
            users,
            new ManagedFileUploads(registry, objects),
            images).ExecuteAsync(
            UserId,
            "avatar.jpg",
            "image/jpeg",
            new byte[] { 1, 2, 3, 4 },
            Now);

        await Assert.That(result.Failure)
            .IsEqualTo(AvatarImageFailure.SourceMetadataMismatch);
        await objects.DidNotReceiveWithAnyArgs()
            .SetObject(default!, default!, default!, default, default);
    }

    [Test]
    public async Task Missing_avatar_object_is_reported_as_not_found()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IStore>();
        users.GetAvatarFileAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new BusinessFileReference(
                Guid.CreateVersion7(),
                "users/missing/avatar",
                "avatar.webp",
                "image/webp"));
        objects.OpenRead("users/missing/avatar", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(null!));

        var result = await new GetUserAvatar(users, objects).ExecuteAsync(UserId);

        await Assert.That(result).IsNull();
    }

    private static UserProfile Profile(
        string? description = null,
        Guid? avatarFileId = null) =>
        new(
            UserId,
            "Player",
            "player@example.test",
            UserRole.User,
            UserKind.Human,
            true,
            description,
            avatarFileId);
}
