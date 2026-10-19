using System.Security.Claims;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Infrastructure.Authentication.Mfa;

namespace NoCTF.API.Security;

public sealed class MfaAuthenticationDeniedException(MfaFailure failure) : Exception("Authentication is no longer sufficient.")
{
    public MfaFailure Failure { get; } = failure;
}

public sealed class CurrentAccessTokenValidator(IMfaAuthenticationStore mfa)
{
    public async Task<bool> IsCurrentAsync(
        ClaimsPrincipal? principal, CancellationToken cancellationToken) => await ValidateAsync(principal, cancellationToken) is null;

    public async Task<MfaFailure?> ValidateAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken) =>
        (await ValidateRequestAsync(principal, cancellationToken)).Failure;

    /// <summary>Validates the request principal and lets the email gate reuse the same database read.</summary>
    public async Task<MfaFailure?> ValidateAsync(
        ClaimsPrincipal? principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateRequestAsync(principal, cancellationToken);
        if (validation.Failure is null && validation.EmailVerificationPending is bool pending)
            EmailVerificationGateMiddleware.SetPending(httpContext, pending);
        return validation.Failure;
    }

    private async Task<MfaRequestValidation> ValidateRequestAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken)
    {
        using var measurement = NoCTF.Application.Observability.NoCtfTelemetry.MeasureAuthentication();
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        var version = principal?.FindFirstValue("token_version");
        if (principal is null || !Guid.TryParse(subject, out var userId) || !int.TryParse(version, out var tokenVersion)
            || principal.FindFirstValue("token_type") != "access") return new(MfaFailure.AccountUnavailable, null);
        var context = AuthenticationContextClaims.Read(principal);
        try
        {
            return mfa is IMfaRequestValidator request
                ? await request.ValidateRequestAsync(userId, tokenVersion, context, cancellationToken)
                : new(await mfa.ValidateContextAsync(userId, tokenVersion, context, cancellationToken), null);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException or System.Security.Cryptography.CryptographicException)
        { return new(MfaFailure.DependencyUnavailable, null); }
    }
}
