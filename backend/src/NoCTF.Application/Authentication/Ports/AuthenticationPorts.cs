using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Ports;

public sealed record AuthenticatedUser(Guid Id, string UserName, string Role, int TokenVersion);
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);
public sealed record RefreshTokenPrincipal(Guid UserId, int TokenVersion);

public interface IUserAuthenticationStore
{
    Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken);
    Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AuthenticatedUser user);
    IssuedRefreshToken IssueRefresh(AuthenticatedUser user);
    RefreshTokenPrincipal? ValidateRefresh(string token);
}
