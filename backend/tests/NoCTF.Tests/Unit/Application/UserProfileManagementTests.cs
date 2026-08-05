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
                Arg.Any<bool>(),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Profile(description: "profile"));
        var update = new UpdateCurrentUserProfile(users);

        await update.ExecuteAsync(UserId, "  profile  ", true, Now);
        await update.ExecuteAsync(UserId, "   ", false, Now);

        await users.Received(1).UpdateProfileAsync(
            UserId,
            "profile",
            true,
            Now,
            Arg.Any<CancellationToken>());
        await users.Received(1).UpdateProfileAsync(
            UserId,
            null,
            false,
            Now,
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, true)]
    [Arguments(false, true, true)]
    public async Task Email_is_projected_only_for_the_owner_administrator_or_public_profile(
        bool isAdministrator,
        bool isEmailPublic,
        bool shouldExposeEmail)
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        users.GetProfileAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Profile(isEmailPublic: isEmailPublic));

        var profile = await new GetPublicUserProfile(users).ExecuteAsync(
            UserId,
            Guid.NewGuid(),
            isAdministrator);

        await Assert.That(profile).IsNotNull();
        await Assert.That(profile!.Email is not null).IsEqualTo(shouldExposeEmail);
        await Assert.That(profile.IsEmailPublic).IsEqualTo(isEmailPublic);
    }

    [Test]
    public async Task Email_is_always_projected_for_the_profile_owner()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        users.GetProfileAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Profile(isEmailPublic: false));

        var profile = await new GetPublicUserProfile(users).ExecuteAsync(
            UserId,
            UserId,
            requesterIsAdministrator: false);

        await Assert.That(profile!.Email).IsEqualTo("player@example.test");
    }

    [Test]
    public async Task Avatar_rejects_content_when_signature_and_content_type_do_not_match()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IObjectStorage>();
        var images = Substitute.For<IAvatarImageProcessor>();
        images.Process(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage));
        var replace = new ReplaceCurrentUserAvatar(users, objects, images);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 1, 2, 3, 4 },
            Now);

        await Assert.That(result.Failure).IsEqualTo(AvatarImageFailure.MalformedImage);
        await objects.DidNotReceiveWithAnyArgs()
            .PutAsync(default!, default!, default!, default!, default);
    }

    [Test]
    public async Task Avatar_switches_metadata_before_cleaning_the_previous_object()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IObjectStorage>();
        var images = Substitute.For<IAvatarImageProcessor>();
        const string storedKey = "users/new-avatar.png";
        const string previousKey = "users/old-avatar.png";
        var normalized = new byte[] { 9, 8, 7 };
        images.Process(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(AvatarImageProcessingResult.Success(new(
                normalized,
                "image/webp",
                "webp",
                "image/png")));
        objects.PutAsync(
                Arg.Any<string>(),
                "avatar.webp",
                "image/webp",
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(new StoredObject(storedKey, "avatar.webp", "image/webp", 3, new string('A', 64)));
        users.ReplaceAvatarAsync(
                UserId,
                storedKey,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(new UserAvatarReplacement(Profile(avatarObjectKey: storedKey), previousKey));
        var replace = new ReplaceCurrentUserAvatar(users, objects, images);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            Now);

        await Assert.That(result.Profile).IsNotNull();
        await users.Received(1).ReplaceAvatarAsync(
            UserId,
            storedKey,
            Now,
            Arg.Any<CancellationToken>());
        await objects.Received(1).DeleteAsync(previousKey, CancellationToken.None);
    }

    [Test]
    public async Task Avatar_rejects_forged_file_metadata_after_successful_decode()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IObjectStorage>();
        var images = Substitute.For<IAvatarImageProcessor>();
        images.Process(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(AvatarImageProcessingResult.Success(new(
                new byte[] { 9, 8, 7 },
                "image/webp",
                "webp",
                "image/png")));

        var result = await new ReplaceCurrentUserAvatar(users, objects, images).ExecuteAsync(
            UserId,
            "avatar.jpg",
            "image/jpeg",
            new byte[] { 1, 2, 3, 4 },
            Now);

        await Assert.That(result.Failure)
            .IsEqualTo(AvatarImageFailure.SourceMetadataMismatch);
        await objects.DidNotReceiveWithAnyArgs()
            .PutAsync(default!, default!, default!, default!, default);
    }

    private static UserProfile Profile(
        string? description = null,
        string? avatarObjectKey = null,
        bool isEmailPublic = false) =>
        new(
            UserId,
            "Player",
            "player@example.test",
            UserRole.User,
            UserKind.Human,
            true,
            description,
            avatarObjectKey,
            isEmailPublic);
}
