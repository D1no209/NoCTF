using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LogoutEndpoint(IConfiguration configuration)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, configuration))
            return TypedResults.NoContent();
        HttpContext.Response.Cookies.Delete("noctf_refresh", new CookieOptions { Path = "/auth" });
        return TypedResults.NoContent();
    }
}
