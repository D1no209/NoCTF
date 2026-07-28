using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Account;

public sealed record AuthenticatedUser(Guid Id, string UserName, string Role, int TokenVersion);
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);
public sealed record RefreshTokenPrincipal(Guid UserId, int TokenVersion);
public sealed record UserProfile(
    Guid Id,
    string UserName,
    string Email,
    string Role,
    bool EmailVerified);
public enum CreateUserState { Created, UserNameConflict, EmailConflict }

public interface IUserAuthenticationStore
{
    Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken);
    Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<CreateUserState> CreateAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> IncrementTokenVersionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AuthenticatedUser user);
    IssuedRefreshToken IssueRefresh(AuthenticatedUser user);
    RefreshTokenPrincipal? ValidateRefresh(string token);
}
