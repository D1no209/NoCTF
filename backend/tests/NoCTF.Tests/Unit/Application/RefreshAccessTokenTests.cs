using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Domain.Identity;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class RefreshAccessTokenTests
{
    [Test]
    public async Task ExecuteAsync_RejectsRefreshTokenAfterTokenVersionChanges()
    {
        var userId = Guid.NewGuid();
        var store = new Store(new AuthenticatedUser(
            userId, "alice", UserRole.User, UserKind.Human, 8));
        var issuer = new Issuer(new RefreshTokenPrincipal(userId, 7));

        var result = await new RefreshAccessToken(store, issuer, TimeProvider.System)
            .ExecuteAsync("refresh-token");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(RefreshAccessTokenFailureCode.RefreshInvalid);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesWhenTokenVersionIsCurrent()
    {
        var user = new AuthenticatedUser(
            Guid.NewGuid(), "alice", UserRole.User, UserKind.Human, 7);
        var store = new Store(user);
        var issuer = new Issuer(new RefreshTokenPrincipal(user.Id, user.TokenVersion));

        var result = await new RefreshAccessToken(store, issuer, TimeProvider.System)
            .ExecuteAsync("refresh-token");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.RefreshToken).IsEqualTo("new-refresh-token");
    }

    [Test]
    public async Task ExecuteAsync_RejectsBotEvenWhenTokenVersionMatches()
    {
        var user = new AuthenticatedUser(
            Guid.NewGuid(), "gitops-bot", UserRole.Organizer, UserKind.Bot, 2);
        var issuer = new Issuer(new RefreshTokenPrincipal(user.Id, user.TokenVersion));

        var result = await new RefreshAccessToken(new Store(user), issuer, TimeProvider.System)
            .ExecuteAsync("refresh-token");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(RefreshAccessTokenFailureCode.RefreshInvalid);
    }

    private sealed class Store(AuthenticatedUser user) : IUserAuthenticationStore
    {
        public Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUser?>(user);

        public Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUser?>(user.Id == userId ? user : null);

        public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserProfile?>(null);
        public Task<UserProfile?> UpdateProfileAsync(Guid userId, string? description,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<UserProfile?>(null);
        public Task<UserAvatarReplacement?> ReplaceAvatarAsync(Guid userId, Guid fileId,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<UserAvatarReplacement?>(null);
        public Task<string?> GetAvatarObjectKeyAsync(Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<CreateUserState> CreateAsync(Guid userId, string userName, string email,
            string password, bool emailVerified, DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateUserState.Created);
        public Task<ChangePasswordState> ChangePasswordAsync(Guid userId, string currentPassword,
            string newPassword, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(ChangePasswordState.CurrentPasswordInvalid);
        public Task<bool> IncrementTokenVersionAsync(Guid userId, DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Issuer(RefreshTokenPrincipal? principal) : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(
            AuthenticatedUser user,
            DateTimeOffset now,
            TimeSpan? lifetime = null,
            Guid? impersonatorUserId = null) =>
            new("access-token", now.Add(lifetime ?? TimeSpan.FromMinutes(15)));

        public IssuedRefreshToken IssueRefresh(AuthenticatedUser user) =>
            new("new-refresh-token", DateTimeOffset.UtcNow.AddDays(30));

        public RefreshTokenPrincipal? ValidateRefresh(string token) => principal;
    }
}
