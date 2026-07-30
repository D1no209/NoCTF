using System.Security.Claims;
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
        return versions.IsCurrentAsync(userId, tokenVersion, cancellationToken);
    }
}
