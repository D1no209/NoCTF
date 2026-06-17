using NoCTF.Infrastructure;

namespace NoCTF.API;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var competitionIdClaim = context.User.FindFirst("competition_id")?.Value;
        if (Guid.TryParse(competitionIdClaim, out var competitionId))
        {
            tenantContext.SetCompetitionId(competitionId);
        }
        else if (TryGetCompetitionIdFromRoute(context.Request.Path, out var routeCompetitionId))
        {
            tenantContext.SetCompetitionId(routeCompetitionId);
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
