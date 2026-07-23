using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Account;

public sealed record RegisterUserCommand(
    string UserName,
    string Email,
    string Password,
    DateTimeOffset Now);

public sealed class RegisterUser(IUserAuthenticationStore store)
{
    public async Task<OperationResult<UserProfile>> ExecuteAsync(
        RegisterUserCommand command,
        CancellationToken ct = default)
    {
        var userName = command.UserName.Trim();
        var email = command.Email.Trim();
        var id = Guid.CreateVersion7(command.Now);
        var state = await store.CreateAsync(
            id, userName, email, command.Password, command.Now, ct);
        if (state != CreateUserState.Created)
            return OperationResult<UserProfile>.Failure(
                state == CreateUserState.EmailConflict
                    ? "email_conflict"
                    : "username_conflict",
                "The requested account identifier is already in use.");
        return OperationResult<UserProfile>.Success(
            new(id, userName, email, "User", false));
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
