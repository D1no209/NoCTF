using NoCTF.Infrastructure;

namespace NoCTF.API;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var competitionIdClaim = context.User.FindFirst("competition_id")?.Value;
        var hasRouteCompetition = TryGetCompetitionIdFromRoute(
            context.Request.Path,
            out var routeCompetitionId);
        if (hasRouteCompetition)
        {
            if (!string.IsNullOrWhiteSpace(competitionIdClaim) &&
                (!Guid.TryParse(competitionIdClaim, out var claimedCompetitionId) ||
                 claimedCompetitionId != routeCompetitionId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            tenantContext.SetCompetitionId(routeCompetitionId);
        }
        else if (Guid.TryParse(competitionIdClaim, out var claimedCompetitionId))
        {
            tenantContext.SetCompetitionId(claimedCompetitionId);
        }

        await next(context);
    }

    private static bool TryGetCompetitionIdFromRoute(PathString path, out Guid competitionId)
    {
        competitionId = Guid.Empty;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "competitions", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(segments[i + 1], out competitionId))
            {
                return true;
            }
        }

        return false;
    }
}
