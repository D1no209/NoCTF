using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Ports;

public sealed record AuthenticatedUser(Guid Id, string UserName, string Role, int TokenVersion);
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
public sealed record RefreshRotation(
    Guid SessionId,
    Guid UserId,
    Guid FamilyId,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    bool ReplayDetected);

public interface IUserAuthenticationStore
{
    Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken);
    Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task<string> CreateRefreshTokenAsync(Guid userId, string? ipAddress, DateTimeOffset now, CancellationToken cancellationToken);
    Task<RefreshRotation?> RotateRefreshAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeRefreshFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeRefreshTokenAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AuthenticatedUser user);
}
