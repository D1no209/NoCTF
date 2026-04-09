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
        else if (context.Request.Headers.TryGetValue("X-Competition-Id", out var headerValue)
                 && Guid.TryParse(headerValue.ToString(), out var headerCompetitionId))
        {
            tenantContext.SetCompetitionId(headerCompetitionId);
        }

        await next(context);
    }
}
