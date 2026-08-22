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
        var cookieName = RefreshCookie.Name(configuration);
        if (!HttpContext.Request.Cookies.TryGetValue(cookieName, out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return TypedResults.Unauthorized();
        }

        var result = await refresh.ExecuteAsync(refreshToken, cancellationToken);
        if (!result.Succeeded)
        {
            // Same __Secure- prefix rule as LogoutEndpoint: the deletion needs the Secure
            // attribute or browsers ignore it.
            HttpContext.Response.Cookies.Delete(
                cookieName,
                RefreshCookie.DeleteOptions(configuration));
            return TypedResults.Unauthorized();
        }

        HttpContext.Response.Cookies.Append(
            cookieName,
            result.Value!.RefreshToken,
            RefreshCookie.Options(configuration));
        return TypedResults.Ok(new RefreshTokenResponse(
            result.Value.UserId,
            result.Value.UserName,
            IdentityProtocolMapper.ToProtocol(result.Value.Role),
            result.Value.EmailVerified,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
