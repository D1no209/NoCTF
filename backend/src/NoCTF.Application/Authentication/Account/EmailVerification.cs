using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Account;

public enum EmailVerificationState
{
    Issued,
    Verified,
    UserNotFound,
    AlreadyVerified,
    InvalidOrExpired
}

public interface IEmailVerificationStore
{
    Task<EmailVerificationState> IssueAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<EmailVerificationState> VerifyAsync(
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ResendEmailVerification(IEmailVerificationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var state = await store.IssueAsync(userId, now, ct);
        return state switch
        {
            EmailVerificationState.Issued => OperationResult.Success(),
            EmailVerificationState.AlreadyVerified => OperationResult.Failure(
                "email_already_verified", "The email address is already verified."),
            _ => OperationResult.Failure("user_not_found", "User was not found.")
        };
    }
}

public sealed class VerifyEmail(IEmailVerificationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        string token,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.VerifyAsync(token, now, ct) == EmailVerificationState.Verified
            ? OperationResult.Success()
            : OperationResult.Failure(
                "email_verification_invalid",
                "The verification token is invalid or expired.");
}
