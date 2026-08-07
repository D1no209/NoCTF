using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Domain.Identity;
using Microsoft.Extensions.Configuration;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record RefreshTokenResponse(
    Guid UserId,
    string UserName,
    UserRoleProtocol Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class RefreshTokenEndpoint(RefreshAccessToken refresh, IConfiguration configuration)
    : EndpointWithoutRequest<Results<Ok<RefreshTokenResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<RefreshTokenResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, configuration))
            return TypedResults.Unauthorized();
        if (!HttpContext.Request.Cookies.TryGetValue("__Secure-noctf_refresh", out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return TypedResults.Unauthorized();
        }

        var result = await refresh.ExecuteAsync(refreshToken, cancellationToken);
        if (!result.Succeeded)
        {
            // Same __Secure- prefix rule as LogoutEndpoint: the deletion needs the Secure
            // attribute or browsers ignore it.
            HttpContext.Response.Cookies.Delete("__Secure-noctf_refresh", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/v1/auth"
            });
            return TypedResults.Unauthorized();
        }

        HttpContext.Response.Cookies.Append("__Secure-noctf_refresh", result.Value!.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            MaxAge = TimeSpan.FromDays(30)
        });
        return TypedResults.Ok(new RefreshTokenResponse(
            result.Value.UserId,
            result.Value.UserName,
            IdentityProtocolMapper.ToProtocol(result.Value.Role),
            result.Value.EmailVerified,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
