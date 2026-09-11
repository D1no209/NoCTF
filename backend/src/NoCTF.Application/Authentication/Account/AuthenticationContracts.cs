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
    Guid? AvatarFileId = null,
    Guid? WallpaperFileId = null,
    bool WallpaperEnabled = false,
    string? SchoolFullName = null,
    string? SchoolStudentNumber = null);
public sealed record PublicUserProfile(
    Guid Id,
    string UserName,
    string? Description,
    Guid? AvatarFileId);
public sealed record UserAvatarReplacement(UserProfile Profile, Guid? PreviousFileId);
public sealed record UserWallpaperReplacement(UserProfile Profile, Guid? PreviousFileId);
public enum UserWallpaperPreferenceState { Updated, UserNotFound, WallpaperNotUploaded }
public sealed record UserWallpaperPreferenceResult(
    UserWallpaperPreferenceState State,
    UserProfile? Profile = null);
public enum CreateUserState { Created, UserNameConflict, EmailConflict }
public enum ChangePasswordState { Changed, CurrentPasswordInvalid }

public sealed record CreateRegisteredUserResult(
    CreateUserState UserState,
    EmailVerificationState VerificationState);

public interface IUserRegistrationStore
{
    Task<CreateRegisteredUserResult> RegisterAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

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
    Task<UserWallpaperReplacement?> ReplaceWallpaperAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<UserWallpaperReplacement?>(null);
    Task<UserWallpaperPreferenceResult> SetWallpaperEnabledAsync(
        Guid userId,
        bool enabled,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new UserWallpaperPreferenceResult(
            UserWallpaperPreferenceState.UserNotFound));
    Task<BusinessFileReference?> GetWallpaperFileAsync(
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
