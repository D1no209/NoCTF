using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshSession;

namespace NoCTF.API.Security;

public sealed class CurrentAccessTokenValidator(IAccessTokenVersionReader versions)
{
    public Task<bool> IsCurrentAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken)
    {
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        var version = principal?.FindFirstValue("token_version");
        if (!Guid.TryParse(subject, out var userId) || !int.TryParse(version, out var tokenVersion))
            return Task.FromResult(false);
        var impersonation = principal?.FindFirstValue(LegacyAccessTokenClaims.Impersonation);
        var impersonator = principal?.FindFirstValue(LegacyAccessTokenClaims.ImpersonatorId);
        LegacyAdministratorIssuedAccessToken? issuedToken = null;
        if (impersonation is not null || impersonator is not null)
        {
            if (!string.Equals(impersonation, "true", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParse(impersonator, out var parsedImpersonatorId)
                || !Guid.TryParse(
                    principal?.FindFirstValue(JwtRegisteredClaimNames.Jti),
                    out var parsedJwtId))
            {
                return Task.FromResult(false);
            }
            issuedToken = new(parsedJwtId, parsedImpersonatorId);
        }
        return versions.IsCurrentAsync(
            userId,
            tokenVersion,
            cancellationToken,
            issuedToken);
    }
}
