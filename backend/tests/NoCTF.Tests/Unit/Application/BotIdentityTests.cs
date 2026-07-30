using NoCTF.Application.Administration;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NSubstitute;

namespace NoCTF.Tests.Unit.Application;

public sealed class BotIdentityTests
{
    [Test]
    public async Task DummyEmail_UsesStableReservedDomainAndUserId()
    {
        var id = Guid.Parse("0190f28d-58b5-7e47-bbb2-fd8f5a2b94cc");

        var email = BotIdentity.DummyEmail(id);

        await Assert.That(email)
            .IsEqualTo("bot-0190f28d58b57e47bbb2fd8f5a2b94cc@bot.invalid");
    }

    [Test]
    public async Task CreateBot_RequiresOrganizerRole()
    {
        var store = Substitute.For<IPlatformAdministrationStore>();
        var platform = new ManagePlatform(
            store,
            Substitute.For<IAccessTokenIssuer>());

        var result = await platform.CreateBotAsync(
            "repository-bot",
            UserRole.User,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(CreateBotState.InvalidRole);
        await store.DidNotReceiveWithAnyArgs()
            .CreateBotAsync(default!, default, default, default);
    }

    [Test]
    [Arguments(59)]
    [Arguments(31_536_001)]
    public async Task IssueBotToken_RejectsLifetimeOutsideBoundedRange(
        long expiresInSeconds)
    {
        var store = Substitute.For<IPlatformAdministrationStore>();
        var platform = new ManagePlatform(
            store,
            Substitute.For<IAccessTokenIssuer>());

        var result = await platform.IssueBotTokenAsync(
            Guid.NewGuid(),
            expiresInSeconds,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Failure)
            .IsEqualTo(IssueBotTokenFailure.InvalidLifetime);
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, default);
    }
}
