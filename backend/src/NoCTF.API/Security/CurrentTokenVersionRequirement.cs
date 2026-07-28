using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NoCTF.Application.Authentication.RefreshSession;

namespace NoCTF.API.Security;

public sealed class CurrentTokenVersionRequirement : IAuthorizationRequirement;

public sealed class CurrentTokenVersionHandler(IAccessTokenVersionReader versions)
    : AuthorizationHandler<CurrentTokenVersionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentTokenVersionRequirement requirement)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        var version = context.User.FindFirstValue("token_version");
        if (!Guid.TryParse(subject, out var userId) || !int.TryParse(version, out var tokenVersion))
            return;

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await versions.IsCurrentAsync(userId, tokenVersion, cancellationToken))
            context.Succeed(requirement);
    }
}
