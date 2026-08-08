using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LogoutEndpoint(IConfiguration configuration)
    : EndpointWithoutRequest<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, configuration))
            return TypedResults.Unauthorized();
        // The deletion must carry the same __Secure- prefix requirements as the original
        // cookie (Secure attribute), otherwise browsers refuse to clear it and logout
        // silently leaves the refresh session alive.
        HttpContext.Response.Cookies.Delete("__Secure-noctf_refresh", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });
        return TypedResults.NoContent();
    }
}
