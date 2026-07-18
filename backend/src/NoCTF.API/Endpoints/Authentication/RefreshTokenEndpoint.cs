using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.RefreshSession;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RefreshTokenEndpoint(RefreshAccessToken refresh)
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
        if (!HttpContext.Request.Cookies.TryGetValue("noctf_refresh", out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return TypedResults.Unauthorized();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var result = await refresh.ExecuteAsync(hash, DateTimeOffset.UtcNow, cancellationToken);
        if (!result.Succeeded)
        {
            HttpContext.Response.Cookies.Delete("noctf_refresh");
            return TypedResults.Unauthorized();
        }

        HttpContext.Response.Cookies.Append("noctf_refresh", result.Value!.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
        return TypedResults.Ok(new LoginResponse(
            result.Value.UserId,
            result.Value.UserName,
            result.Value.Role,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
