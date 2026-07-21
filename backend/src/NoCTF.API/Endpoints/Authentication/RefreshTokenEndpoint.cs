using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.RefreshJwt;
using Microsoft.Extensions.Configuration;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RefreshTokenEndpoint(RefreshAccessToken refresh, IConfiguration configuration)
    : EndpointWithoutRequest<Results<Ok<LoginResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, configuration))
            return TypedResults.Unauthorized();
        if (!HttpContext.Request.Cookies.TryGetValue("noctf_refresh", out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return TypedResults.Unauthorized();
        }

        var result = await refresh.ExecuteAsync(refreshToken, cancellationToken);
        if (!result.Succeeded)
        {
            HttpContext.Response.Cookies.Delete("noctf_refresh", new CookieOptions { Path = "/auth" });
            return TypedResults.Unauthorized();
        }

        HttpContext.Response.Cookies.Append("noctf_refresh", result.Value!.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = configuration.GetValue("Authentication:RefreshCookieSecure", true),
            SameSite = SameSiteMode.Strict,
            Path = "/auth",
            MaxAge = TimeSpan.FromDays(30)
        });
        return TypedResults.Ok(new LoginResponse(
            result.Value.UserId,
            result.Value.UserName,
            result.Value.Role,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
