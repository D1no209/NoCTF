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
        HttpContext.Response.Cookies.Delete("__Secure-noctf_refresh", new CookieOptions { Path = "/api/v1/auth" });
        return TypedResults.NoContent();
    }
}
