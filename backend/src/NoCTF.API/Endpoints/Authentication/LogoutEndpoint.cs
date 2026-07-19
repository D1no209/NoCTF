using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Logout;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LogoutEndpoint(LogoutUser logout)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsSameOrigin(HttpContext.Request))
            return TypedResults.NoContent();
        if (HttpContext.Request.Cookies.TryGetValue("noctf_refresh", out var refreshToken)
            && !string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
            await logout.ExecuteAsync(hash, DateTimeOffset.UtcNow, cancellationToken);
        }

        HttpContext.Response.Cookies.Delete("noctf_refresh", new CookieOptions { Path = "/auth/refresh" });
        return TypedResults.NoContent();
    }
}
