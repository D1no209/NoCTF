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
    public async Task CreateBot_AcceptsLeastPrivilegeNotificationRelayRole()
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
            UserRole.User,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsNotEqualTo(CreateBotState.InvalidRole);
        await store.Received(1).CreateBotAsync(
            "repository-bot",
            UserRole.User,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateBot_RejectsAdministratorRole()
    {
        var store = Substitute.For<IPlatformAdministrationStore>();
        var platform = new ManagePlatform(
            store,
            Substitute.For<IAccessTokenIssuer>());

        var result = await platform.CreateBotAsync(
            "administrator-bot",
            UserRole.Administrator,
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(CreateBotState.InvalidRole);
        await store.DidNotReceiveWithAnyArgs()
            .CreateBotAsync(default!, default, default, default);
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
            Guid.NewGuid(),
            expiresInSeconds,
            "support investigation",
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
            Guid.NewGuid(),
            3600,
            "support investigation",
            now);

        await Assert.That(result.Failure)
            .IsEqualTo(IssuePlatformUserTokenFailure.AccountInactive);
        tokenIssuer.DidNotReceiveWithAnyArgs().Issue(default!, default, default);
    }

    [Test]
    [Arguments(UserKind.Human, UserRole.User)]
    [Arguments(UserKind.Bot, UserRole.Organizer)]
    [Arguments(UserKind.Human, UserRole.Administrator)]
    public async Task IssueUserToken_AcceptsEveryActiveTargetAndPersistsTrimmedAuditBeforeReturning(
        UserKind kind,
        UserRole role)
    {
        var userId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
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
                TimeSpan.FromHours(1),
                actorId)
            .Returns(new IssuedAccessToken("token", now.AddHours(1), jwtId));
        var platform = new ManagePlatform(store, issuer);

        var result = await platform.IssueUserTokenAsync(
            userId,
            actorId,
            3600,
            "  support case  ",
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
            TimeSpan.FromHours(1),
            actorId);
        await store.Received(1).RecordTokenIssuedAsync(
            actorId,
            Arg.Is<PlatformUserTokenAuditFact>(fact =>
                fact != null
                && fact.TargetUserId == userId
                && fact.Action == PlatformUserTokenAdministrationAction.AccessTokenIssued
                && fact.JwtId == jwtId
                && fact.Reason == "support case"
                && fact.TokenVersion == 6),
            now,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IssueUserToken_RejectsBlankReasonBeforeReadingAccount()
    {
        var store = Substitute.For<IPlatformAdministrationStore>();
        var platform = new ManagePlatform(store, Substitute.For<IAccessTokenIssuer>());

        var result = await platform.IssueUserTokenAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3600,
            " ",
            DateTimeOffset.UtcNow);

        await Assert.That(result.Failure)
            .IsEqualTo(IssuePlatformUserTokenFailure.ReasonInvalid);
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, default);
    }

    [Test]
    public async Task IssueUserToken_DoesNotReturnWhenPermanentAuditFails()
    {
        var userId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformAdministrationStore>();
        store.FindUserAsync(userId, Arg.Any<CancellationToken>()).Returns(new PlatformUserView(
            userId,
            "target",
            "target@example.test",
            UserKind.Human,
            UserRole.User,
            UserAccountStatus.Active,
            0,
            true,
            now,
            now));
        store.RecordTokenIssuedAsync(
                actorId,
                Arg.Any<PlatformUserTokenAuditFact>(),
                now,
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("audit unavailable"));
        var issuer = Substitute.For<IAccessTokenIssuer>();
        issuer.Issue(
                Arg.Any<AuthenticatedUser>(),
                now,
                TimeSpan.FromHours(1),
                actorId)
            .Returns(new IssuedAccessToken("not-returned", now.AddHours(1), Guid.NewGuid()));
        var platform = new ManagePlatform(store, issuer);

        await Assert.That(async () => await platform.IssueUserTokenAsync(
                userId,
                actorId,
                3600,
                "support case",
                now))
            .Throws<InvalidOperationException>();
    }
}
