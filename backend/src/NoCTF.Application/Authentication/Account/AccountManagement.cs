using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.Account;

public sealed record RegisterUserCommand(
    string UserName,
    string Email,
    string Password,
    DateTimeOffset Now);

public sealed record RegisterUserResult(
    UserProfile Profile,
    bool RequiresEmailVerification,
    bool VerificationEmailQueued);

public enum RegisterUserFailureCode
{
    UserNameConflict,
    EmailConflict
}

public enum LogoutAllFailureCode
{
    UserNotFound
}

public sealed class RegisterUser(
    IUserAuthenticationStore store,
    IEmailVerificationStore? emailVerification = null)
{
    public async Task<OperationResult<RegisterUserResult, RegisterUserFailureCode>> ExecuteAsync(
        RegisterUserCommand command,
        CancellationToken ct = default)
    {
        var userName = command.UserName.Trim();
        var email = EmailCanonicalizer.Canonicalize(command.Email);
        var id = Guid.CreateVersion7(command.Now);
        var requiresEmailVerification = emailVerification is not null
            && await emailVerification.IsRequiredAsync(ct);
        var state = await store.CreateAsync(
            id,
            userName,
            email,
            command.Password,
            emailVerified: !requiresEmailVerification,
            command.Now,
            ct);
        if (state != CreateUserState.Created)
            return OperationResult<RegisterUserResult, RegisterUserFailureCode>.Failure(
                state == CreateUserState.EmailConflict
                    ? RegisterUserFailureCode.EmailConflict
                    : RegisterUserFailureCode.UserNameConflict,
                "The requested account identifier is already in use.");

        var profile = new UserProfile(
            id,
            userName,
            email,
            UserRole.User,
            UserKind.Human,
            EmailVerified: !requiresEmailVerification);
        if (!requiresEmailVerification || emailVerification is null)
            return OperationResult<RegisterUserResult, RegisterUserFailureCode>.Success(new(profile, false, false));

        var verificationState = await emailVerification.IssueAsync(id, command.Now, ct);
        if (verificationState == EmailVerificationState.Disabled)
            profile = profile with { EmailVerified = true };
        return OperationResult<RegisterUserResult, RegisterUserFailureCode>.Success(new(
            profile,
            verificationState is not EmailVerificationState.Disabled,
            verificationState == EmailVerificationState.Issued));
    }
}

public sealed class GetCurrentUser(IUserAuthenticationStore store)
{
    public Task<UserProfile?> ExecuteAsync(Guid userId, CancellationToken ct = default) =>
        store.GetProfileAsync(userId, ct);
}

public sealed class GetPublicUserProfile(IUserAuthenticationStore store)
{
    public async Task<PublicUserProfile?> ExecuteAsync(
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var profile = await store.GetProfileAsync(targetUserId, ct);
        if (profile is null)
            return null;

        return new(
            profile.Id,
            profile.UserName,
            profile.Description,
            profile.AvatarFileId);
    }
}

public sealed class ChangePassword(IUserAuthenticationStore store)
{
    public Task<ChangePasswordState> ExecuteAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.ChangePasswordAsync(userId, currentPassword, newPassword, now, ct);
}

public sealed class LogoutAll(IUserAuthenticationStore store)
{
    public async Task<OperationResult<LogoutAllFailureCode>> ExecuteAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.IncrementTokenVersionAsync(userId, now, ct)
            ? OperationResult<LogoutAllFailureCode>.Success()
            : OperationResult<LogoutAllFailureCode>.Failure(
                LogoutAllFailureCode.UserNotFound,
                "User was not found.");
}
