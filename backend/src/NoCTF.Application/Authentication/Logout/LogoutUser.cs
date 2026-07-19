using NoCTF.Application.Authentication.Ports;

namespace NoCTF.Application.Authentication.Logout;

public sealed class LogoutUser(IUserAuthenticationStore store)
{
    public Task ExecuteAsync(string refreshTokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        store.RevokeRefreshTokenAsync(refreshTokenHash, now, cancellationToken);
}
