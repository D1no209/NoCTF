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
    IUserRegistrationStore store,
    NoCTF.Application.Admission.ICredentialWorkAdmission? admission = null)
{
    public async Task<OperationResult<RegisterUserResult, RegisterUserFailureCode>> ExecuteAsync(
        RegisterUserCommand command,
        CancellationToken ct = default)
    {
        var userName = command.UserName.Trim();
        var email = EmailCanonicalizer.Canonicalize(command.Email);
        await using var lease = admission is null ? null : await admission.AcquireAsync(email, ct);
        ct = lease?.Token ?? ct;
        var id = Guid.CreateVersion7(command.Now);
        var result = await store.RegisterAsync(
            id,
            userName,
            email,
            command.Password,
            command.Now,
            ct);
        if (result.UserState != CreateUserState.Created)
            return OperationResult<RegisterUserResult, RegisterUserFailureCode>.Failure(
                result.UserState == CreateUserState.EmailConflict
                    ? RegisterUserFailureCode.EmailConflict
                    : RegisterUserFailureCode.UserNameConflict,
                "The requested account identifier is already in use.");

        var requiresEmailVerification = result.VerificationState
            is not EmailVerificationState.Disabled;
        var profile = new UserProfile(
            id,
            userName,
            email,
            UserRole.User,
            UserKind.Human,
            EmailVerified: !requiresEmailVerification);
        return OperationResult<RegisterUserResult, RegisterUserFailureCode>.Success(new(
            profile,
            requiresEmailVerification,
            result.VerificationState == EmailVerificationState.Issued));
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

public sealed class ChangePassword(IUserAuthenticationStore store, NoCTF.Application.Admission.ICredentialWorkAdmission? admission = null)
{
    public async Task<ChangePasswordState> ExecuteAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await using var lease = admission is null ? null : await admission.AcquireAsync(userId.ToString("N"), ct);
        return await store.ChangePasswordAsync(userId, currentPassword, newPassword, now, lease?.Token ?? ct);
    }
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
