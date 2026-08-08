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

public enum EmailVerificationFailureCode
{
    EmailAlreadyVerified,
    EmailVerificationDisabled,
    EmailDeliveryNotConfigured,
    EmailVerificationRateLimited,
    UserNotFound,
    EmailVerificationInvalid
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
    public async Task<OperationResult<EmailVerificationFailureCode>> ExecuteAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var state = await store.IssueAsync(userId, now, ct);
        return state switch
        {
            EmailVerificationState.Issued => OperationResult<EmailVerificationFailureCode>.Success(),
            EmailVerificationState.AlreadyVerified => OperationResult<EmailVerificationFailureCode>.Failure(
                EmailVerificationFailureCode.EmailAlreadyVerified, "The email address is already verified."),
            EmailVerificationState.Disabled => OperationResult<EmailVerificationFailureCode>.Failure(
                EmailVerificationFailureCode.EmailVerificationDisabled, "Email verification is disabled."),
            EmailVerificationState.DeliveryNotConfigured => OperationResult<EmailVerificationFailureCode>.Failure(
                EmailVerificationFailureCode.EmailDeliveryNotConfigured, "Email delivery is not configured."),
            EmailVerificationState.RateLimited => OperationResult<EmailVerificationFailureCode>.Failure(
                EmailVerificationFailureCode.EmailVerificationRateLimited,
                "A verification email was sent recently. Try again later."),
            _ => OperationResult<EmailVerificationFailureCode>.Failure(
                EmailVerificationFailureCode.UserNotFound, "User was not found.")
        };
    }
}

public sealed class VerifyEmail(IEmailVerificationStore store)
{
    public async Task<OperationResult<EmailVerificationFailureCode>> ExecuteAsync(
        string token,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.VerifyAsync(token, now, ct) == EmailVerificationState.Verified
            ? OperationResult<EmailVerificationFailureCode>.Success()
            : OperationResult<EmailVerificationFailureCode>.Failure(
                EmailVerificationFailureCode.EmailVerificationInvalid,
                "The verification token is invalid or expired.");
}
