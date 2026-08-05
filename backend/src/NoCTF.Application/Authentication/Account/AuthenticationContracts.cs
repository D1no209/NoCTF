using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.Account;

public sealed record AuthenticatedUser(
    Guid Id,
    string UserName,
    UserRole Role,
    UserKind Kind,
    int TokenVersion,
    bool EmailVerified = true);
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);
public sealed record RefreshTokenPrincipal(Guid UserId, int TokenVersion);
public sealed record UserProfile(
    Guid Id,
    string UserName,
    string Email,
    UserRole Role,
    UserKind Kind,
    bool EmailVerified,
    string? Description = null,
    string? AvatarObjectKey = null,
    bool IsEmailPublic = false);
public sealed record PublicUserProfile(
    Guid Id,
    string UserName,
    string? Email,
    string? Description,
    string? AvatarObjectKey,
    bool IsEmailPublic);
public sealed record UserAvatarReplacement(UserProfile Profile, string? PreviousObjectKey);
public enum CreateUserState { Created, UserNameConflict, EmailConflict }
public enum ChangePasswordState { Changed, CurrentPasswordInvalid }

public interface IUserAuthenticationStore
{
    Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken);
    Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile?> UpdateProfileAsync(
        Guid userId,
        string? description,
        bool isEmailPublic,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<UserAvatarReplacement?> ReplaceAvatarAsync(
        Guid userId,
        string objectKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<string?> GetAvatarObjectKeyAsync(Guid userId, CancellationToken cancellationToken);
    Task<CreateUserState> CreateAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChangePasswordState> ChangePasswordAsync(
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
    IssuedAccessToken Issue(
        AuthenticatedUser user,
        DateTimeOffset now,
        TimeSpan? lifetime = null);
    IssuedRefreshToken IssueRefresh(AuthenticatedUser user);
    RefreshTokenPrincipal? ValidateRefresh(string token);
}
