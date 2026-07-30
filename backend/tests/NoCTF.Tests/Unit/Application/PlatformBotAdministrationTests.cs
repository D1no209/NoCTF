using NoCTF.Application.Administration;
using NoCTF.Application.Administration.Bots;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.Application;

public sealed class PlatformBotAdministrationTests
{
    [Test]
    public async Task Create_accepts_only_Organizer_Bots()
    {
        var store = new Store();
        var create = new CreatePlatformBot(store);

        var rejected = await create.ExecuteAsync(
            "repository-bot",
            UserRole.Administrator,
            DateTimeOffset.UtcNow);
        var created = await create.ExecuteAsync(
            "repository-bot",
            UserRole.Organizer,
            DateTimeOffset.UtcNow);

        await Assert.That(rejected.ErrorCode).IsEqualTo("invalid_bot_role");
        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(created.Value!.Kind).IsEqualTo(UserKind.Bot);
    }

    [Test]
    public async Task Token_uses_requested_bounded_lifetime_and_current_version()
    {
        var botId = Guid.NewGuid();
        var issuer = new Issuer();
        var issue = new IssuePlatformBotToken(
            new Store(new(botId, "repository-bot", UserRole.Organizer, 9)),
            issuer);

        var result = await issue.ExecuteAsync(botId, 31_536_000);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(issuer.User)
            .IsEqualTo(new AuthenticatedUser(botId, "repository-bot", "Organizer", 9));
        await Assert.That(issuer.Lifetime).IsEqualTo(TimeSpan.FromDays(365));
    }

    [Test]
    public async Task Token_rejects_unbounded_lifetime()
    {
        var result = await new IssuePlatformBotToken(new Store(), new Issuer())
            .ExecuteAsync(Guid.NewGuid(), 31_536_001);

        await Assert.That(result.ErrorCode).IsEqualTo("invalid_token_lifetime");
    }

    private sealed class Store(PlatformBotTokenSubject? subject = null) : IPlatformBotStore
    {
        public Task<(CreatePlatformBotState State, PlatformUserView? Bot)> CreateAsync(
            CreatePlatformBotCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult((
                CreatePlatformBotState.Created,
                (PlatformUserView?)new(
                    command.BotId,
                    command.UserName,
                    $"bot-{command.BotId:N}@bots.invalid",
                    UserKind.Bot,
                    command.Role,
                    0,
                    false,
                    command.Now,
                    command.Now)));

        public Task<PlatformBotTokenSubject?> FindTokenSubjectAsync(
            Guid botId,
            CancellationToken cancellationToken) =>
            Task.FromResult(subject?.Id == botId ? subject : null);
    }

    private sealed class Issuer : IAccessTokenIssuer
    {
        public AuthenticatedUser? User { get; private set; }
        public TimeSpan? Lifetime { get; private set; }

        public IssuedAccessToken Issue(AuthenticatedUser user) =>
            Issue(user, TimeSpan.FromMinutes(15));

        public IssuedAccessToken Issue(AuthenticatedUser user, TimeSpan lifetime)
        {
            User = user;
            Lifetime = lifetime;
            return new("access-token", DateTimeOffset.UtcNow.Add(lifetime));
        }

        public IssuedRefreshToken IssueRefresh(AuthenticatedUser user) =>
            throw new NotSupportedException();

        public RefreshTokenPrincipal? ValidateRefresh(string token) => null;
    }
}
