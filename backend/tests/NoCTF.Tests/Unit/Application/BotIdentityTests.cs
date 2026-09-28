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
    [Arguments(UserRole.User)]
    [Arguments(UserRole.Organizer)]
    [Arguments(UserRole.Administrator)]
    public async Task CreateBot_AcceptsEveryDefinedRole(UserRole role)
    {
        var store = Substitute.For<IPlatformAdministrationStore>();
        store.CreateBotAsync(
                Arg.Any<string>(),
                Arg.Any<UserRole>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateBotResult(CreateBotState.Created));
        var platform = new ManagePlatform(
            store,
            Substitute.For<IAccessTokenIssuer>());

        var result = await platform.CreateBotAsync(
            "repository-bot",
            role,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsNotEqualTo(CreateBotState.InvalidRole);
        await store.Received(1).CreateBotAsync(
            "repository-bot",
            role,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(59)]
    [Arguments(31_536_001)]
    public async Task IssueUserToken_RejectsLifetimeOutsideBoundedRange(
        long expiresInSeconds)
    {
        var store = Substitute.For<IPlatformAdministrationStore>();
        var platform = new ManagePlatform(
            store,
            Substitute.For<IAccessTokenIssuer>());

        var result = await platform.IssueUserTokenAsync(
            Guid.NewGuid(),
            expiresInSeconds,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Failure)
            .IsEqualTo(IssuePlatformUserTokenFailure.InvalidLifetime);
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, default);
    }

    [Test]
    public async Task IssueUserToken_RejectsInactiveAccount()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformAdministrationStore>();
        store.FindUserAsync(userId, Arg.Any<CancellationToken>()).Returns(new PlatformUserView(
            userId,
            "retired-bot",
            BotIdentity.DummyEmail(userId),
            UserKind.Bot,
            UserRole.User,
            UserAccountStatus.Anonymized,
            4,
            false,
            now,
            now));
        var tokenIssuer = Substitute.For<IAccessTokenIssuer>();
        var platform = new ManagePlatform(store, tokenIssuer);

        var result = await platform.IssueUserTokenAsync(
            userId,
            3600,
            now);

        await Assert.That(result.Failure)
            .IsEqualTo(IssuePlatformUserTokenFailure.AccountInactive);
        tokenIssuer.DidNotReceiveWithAnyArgs().Issue(default!, default, default);
    }

    [Test]
    [Arguments(UserKind.Human, UserRole.User)]
    [Arguments(UserKind.Bot, UserRole.Organizer)]
    [Arguments(UserKind.Human, UserRole.Administrator)]
    public async Task IssueUserToken_AcceptsEveryActiveTargetAsAnOrdinaryAccessToken(
        UserKind kind,
        UserRole role)
    {
        var userId = Guid.NewGuid();
        var jwtId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-09-12T01:00:00Z");
        var user = new PlatformUserView(
            userId,
            "target-user",
            "target@example.test",
            kind,
            role,
            UserAccountStatus.Active,
            6,
            false,
            now.AddDays(-1),
            now);
        var store = Substitute.For<IPlatformAdministrationStore>();
        store.FindUserAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        var issuer = Substitute.For<IAccessTokenIssuer>();
        issuer.Issue(
                Arg.Any<AuthenticatedUser>(),
                now,
                TimeSpan.FromHours(1))
            .Returns(new IssuedAccessToken("token", now.AddHours(1), jwtId));
        var platform = new ManagePlatform(store, issuer);

        var result = await platform.IssueUserTokenAsync(
            userId,
            3600,
            now);

        await Assert.That(result.Failure).IsEqualTo(IssuePlatformUserTokenFailure.None);
        await Assert.That(result.TargetUser).IsEqualTo(user);
        issuer.Received(1).Issue(
            Arg.Is<AuthenticatedUser>(target =>
                target != null
                && target.Id == userId
                && target.Role == role
                && target.Kind == kind
                && target.TokenVersion == 6
                && !target.EmailVerified),
            now,
            TimeSpan.FromHours(1));
    }
}
