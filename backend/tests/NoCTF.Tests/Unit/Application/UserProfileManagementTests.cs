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
    public async Task Avatar_rejects_content_when_signature_and_content_type_do_not_match()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IObjectStorage>();
        var replace = new ReplaceCurrentUserAvatar(users, objects);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 1, 2, 3, 4 },
            Now);

        await Assert.That(result.ErrorCode).IsEqualTo("avatar_format_invalid");
        await objects.DidNotReceiveWithAnyArgs()
            .PutAsync(default!, default!, default!, default!, default);
    }

    [Test]
    public async Task Avatar_switches_metadata_before_cleaning_the_previous_object()
    {
        var users = Substitute.For<IUserAuthenticationStore>();
        var objects = Substitute.For<IObjectStorage>();
        const string storedKey = "users/new-avatar.png";
        const string previousKey = "users/old-avatar.png";
        objects.PutAsync(
                Arg.Any<string>(),
                "avatar.png",
                "image/png",
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(new StoredObject(storedKey, "avatar.png", "image/png", 8, new string('A', 64)));
        users.ReplaceAvatarAsync(
                UserId,
                storedKey,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(new UserAvatarReplacement(Profile(avatarObjectKey: storedKey), previousKey));
        var replace = new ReplaceCurrentUserAvatar(users, objects);

        var result = await replace.ExecuteAsync(
            UserId,
            "avatar.png",
            "image/png",
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            Now);

        await Assert.That(result.Succeeded).IsTrue();
        await users.Received(1).ReplaceAvatarAsync(
            UserId,
            storedKey,
            Now,
            Arg.Any<CancellationToken>());
        await objects.Received(1).DeleteAsync(previousKey, CancellationToken.None);
    }

    private static UserProfile Profile(
        string? description = null,
        string? avatarObjectKey = null) =>
        new(
            UserId,
            "Player",
            "player@example.test",
            UserRole.User,
            UserKind.Human,
            true,
            description,
            avatarObjectKey);
}
