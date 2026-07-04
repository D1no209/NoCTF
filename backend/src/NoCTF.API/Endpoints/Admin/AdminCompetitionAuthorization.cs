using System.Security.Claims;
using NoCTF.API.Permissions;

namespace NoCTF.API.Endpoints.Admin;

internal static class AdminCompetitionAuthorization
{
    public static bool TryGetUserId(HttpContext httpContext, out Guid userId)
    {
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out userId);
    }

    public static async Task<bool> CanManageAsync(
        HttpContext httpContext,
        ICompetitionPermissionService permissions,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        return TryGetUserId(httpContext, out var userId) &&
               await permissions.CanManageCompetitionAsync(userId, competitionId, cancellationToken);
    }
}
