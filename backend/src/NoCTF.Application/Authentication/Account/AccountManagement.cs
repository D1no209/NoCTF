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

public sealed class RegisterUser(
    IUserAuthenticationStore store,
    IEmailVerificationStore? emailVerification = null)
{
    public async Task<OperationResult<RegisterUserResult>> ExecuteAsync(
        RegisterUserCommand command,
        CancellationToken ct = default)
    {
        var userName = command.UserName.Trim();
        var email = command.Email.Trim();
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
            return OperationResult<RegisterUserResult>.Failure(
                state == CreateUserState.EmailConflict
                    ? "email_conflict"
                    : "username_conflict",
                "The requested account identifier is already in use.");

        var profile = new UserProfile(
            id,
            userName,
            email,
            UserRole.User,
            UserKind.Human,
            EmailVerified: !requiresEmailVerification);
        if (!requiresEmailVerification || emailVerification is null)
            return OperationResult<RegisterUserResult>.Success(new(profile, false, false));

        var verificationState = await emailVerification.IssueAsync(id, command.Now, ct);
        if (verificationState == EmailVerificationState.Disabled)
            profile = profile with { EmailVerified = true };
        return OperationResult<RegisterUserResult>.Success(new(
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

public sealed class ChangePassword(IUserAuthenticationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.ChangePasswordAsync(userId, currentPassword, newPassword, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure(
                "current_password_invalid",
                "The current password is invalid.");
}

public sealed class LogoutAll(IUserAuthenticationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.IncrementTokenVersionAsync(userId, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("user_not_found", "User was not found.");
}
