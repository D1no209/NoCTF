using NoCTF.Application.Common;
using NoCTF.Domain.Identity;
using NoCTF.Application.Storage;

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
    Guid? AvatarFileId = null);
public sealed record PublicUserProfile(
    Guid Id,
    string UserName,
    string? Description,
    Guid? AvatarFileId);
public sealed record UserAvatarReplacement(UserProfile Profile, Guid? PreviousFileId);
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
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<UserAvatarReplacement?> ReplaceAvatarAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<BusinessFileReference?> GetAvatarFileAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<BusinessFileReference?>(null);
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
