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
        CancellationToken cancellationToken)
    {
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        var version = principal?.FindFirstValue("token_version");
        if (principal is null || !Guid.TryParse(subject, out var userId) || !int.TryParse(version, out var tokenVersion)
            || principal.FindFirstValue("token_type") != "access") return MfaFailure.AccountUnavailable;
        return await mfa.ValidateContextAsync(userId, tokenVersion, AuthenticationContextClaims.Read(principal), cancellationToken);
    }
}
