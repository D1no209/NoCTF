using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Authentication.RefreshJwt;

namespace NoCTF.Tests.Unit.Application;

public sealed class RefreshAccessTokenTests
{
    [Test]
    public async Task ExecuteAsync_RejectsRefreshTokenAfterTokenVersionChanges()
    {
        var userId = Guid.NewGuid();
        var store = new Store(new AuthenticatedUser(userId, "alice", "User", 8));
        var issuer = new Issuer(new RefreshTokenPrincipal(userId, 7));

        var result = await new RefreshAccessToken(store, issuer)
            .ExecuteAsync("refresh-token");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo("refresh_invalid");
    }

    [Test]
    public async Task ExecuteAsync_RefreshesWhenTokenVersionIsCurrent()
    {
        var user = new AuthenticatedUser(Guid.NewGuid(), "alice", "User", 7);
        var store = new Store(user);
        var issuer = new Issuer(new RefreshTokenPrincipal(user.Id, user.TokenVersion));

        var result = await new RefreshAccessToken(store, issuer)
            .ExecuteAsync("refresh-token");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.RefreshToken).IsEqualTo("new-refresh-token");
    }

    private sealed class Store(AuthenticatedUser user) : IUserAuthenticationStore
    {
        public Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUser?>(user);

        public Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUser?>(user.Id == userId ? user : null);
    }

    private sealed class Issuer(RefreshTokenPrincipal? principal) : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(AuthenticatedUser user) =>
            new("access-token", DateTimeOffset.UtcNow.AddMinutes(15));

        public IssuedRefreshToken IssueRefresh(AuthenticatedUser user) =>
            new("new-refresh-token", DateTimeOffset.UtcNow.AddDays(30));

        public RefreshTokenPrincipal? ValidateRefresh(string token) => principal;
    }
}
