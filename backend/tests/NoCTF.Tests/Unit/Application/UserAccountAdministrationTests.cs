using NSubstitute;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class UserAccountAdministrationTests
{
    [Test]
    public async Task Deletion_requires_a_meaningful_reason_before_store_mutation()
    {
        var store = Substitute.For<IUserAccountAdministrationStore>();
        var objects = Substitute.For<IObjectStorage>();
        var accounts = new ManageUserAccounts(store, objects);

        var result = await accounts.DeleteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            UserDeletionMode.HardDelete,
            "  ",
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(UserDeletionState.ReasonInvalid);
        await store.DidNotReceiveWithAnyArgs().DeleteAsync(
            default,
            default,
            default,
            default!,
            default,
            default);
    }

    [Test]
    public async Task Successful_anonymization_cleans_the_detached_avatar_object()
    {
        var store = Substitute.For<IUserAccountAdministrationStore>();
        var objects = Substitute.For<IObjectStorage>();
        const string avatarObjectKey = "users/avatar.webp";
        store.DeleteAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                UserDeletionMode.Anonymize,
                "Account closure",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(new UserDeletionStoreResult(
                UserDeletionState.Anonymized,
                AvatarObjectKey: avatarObjectKey));
        var accounts = new ManageUserAccounts(store, objects);

        var result = await accounts.DeleteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            UserDeletionMode.Anonymize,
            "  Account closure  ",
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(UserDeletionState.Anonymized);
        await objects.Received(1).DeleteAsync(avatarObjectKey, CancellationToken.None);
    }
}
