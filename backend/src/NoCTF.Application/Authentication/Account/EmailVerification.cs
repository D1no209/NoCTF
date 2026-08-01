using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Account;

public enum EmailVerificationState
{
    Issued,
    Verified,
    Disabled,
    DeliveryNotConfigured,
    RateLimited,
    UserNotFound,
    AlreadyVerified,
    InvalidOrExpired
}

public interface IEmailVerificationStore
{
    Task<bool> IsRequiredAsync(CancellationToken cancellationToken);
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
            EmailVerificationState.Disabled => OperationResult.Failure(
                "email_verification_disabled", "Email verification is disabled."),
            EmailVerificationState.DeliveryNotConfigured => OperationResult.Failure(
                "email_delivery_not_configured", "Email delivery is not configured."),
            EmailVerificationState.RateLimited => OperationResult.Failure(
                "email_verification_rate_limited",
                "A verification email was sent recently. Try again later."),
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
