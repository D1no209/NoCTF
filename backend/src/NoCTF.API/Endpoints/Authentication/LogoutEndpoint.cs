using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LogoutEndpoint(IOptions<RefreshHttpOptions> options)
    : EndpointWithoutRequest<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value))
            return TypedResults.Unauthorized();
        HttpContext.Response.Cookies.Delete(
            RefreshCookie.Name(options.Value),
            RefreshCookie.DeleteOptions(options.Value));
        return TypedResults.NoContent();
    }
}
