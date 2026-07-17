using FastEndpoints;
using NoCTF.API.Auth;
using NoCTF.Core;

namespace NoCTF.API.Endpoints.Auth;

public sealed class ResendEmailVerificationRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResendEmailVerificationEndpoint(IEmailVerificationService emailVerification)
    : Endpoint<ResendEmailVerificationRequest>
{
    public override void Configure()
    {
        Post("/api/auth/email-verification/resend");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("auth-email-verification"));
    }

    public override async Task HandleAsync(ResendEmailVerificationRequest req, CancellationToken ct)
    {
        if (req.Email.Length > UserInputLimits.EmailMaxLength)
        {
            AddError(request => request.Email, "Email is invalid.");
            await SendErrorsAsync(400, ct);
            return;
        }

        await emailVerification.ResendAsync(req.Email, ct);
        await SendStringAsync("email_verification_requested", 202, cancellation: ct);
    }
}

public sealed class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public sealed class VerifyEmailResponse
{
    public string Status { get; set; } = string.Empty;
}

public sealed class VerifyEmailEndpoint(IEmailVerificationService emailVerification)
    : Endpoint<VerifyEmailRequest, VerifyEmailResponse>
{
    public override void Configure()
    {
        Post("/api/auth/email-verification/verify");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("auth-email-verification"));
    }

    public override async Task HandleAsync(VerifyEmailRequest req, CancellationToken ct)
    {
        var result = await emailVerification.VerifyAsync(req.Token, ct);
        switch (result)
        {
            case EmailVerificationAttempt.Verified:
                await SendAsync(new VerifyEmailResponse { Status = "verified" }, cancellation: ct);
                return;
            case EmailVerificationAttempt.AlreadyVerified:
                await SendAsync(new VerifyEmailResponse { Status = "already_verified" }, cancellation: ct);
                return;
            case EmailVerificationAttempt.Expired:
                await SendStringAsync("email_verification_token_expired", 400, cancellation: ct);
                return;
            case EmailVerificationAttempt.Disabled:
                await SendStringAsync("email_verification_disabled", 409, cancellation: ct);
                return;
            default:
                await SendStringAsync("email_verification_token_invalid", 400, cancellation: ct);
                return;
        }
    }
}
