using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Domain.Identity;
using Microsoft.Extensions.Options;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record RefreshTokenResponse(
    Guid UserId,
    string UserName,
    UserRoleProtocol Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class RefreshTokenEndpoint(RefreshAccessToken refresh, IOptions<RefreshHttpOptions> options)
    : EndpointWithoutRequest<Results<Ok<RefreshTokenResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Exchanges a valid refresh cookie for a new access token.";
            summary.Description = summary.Summary;
        });

        Post("/auth/refresh");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<RefreshTokenResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value))
            return TypedResults.Unauthorized();
        var cookieName = RefreshCookie.Name(options.Value);
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
                RefreshCookie.DeleteOptions(options.Value));
            return TypedResults.Unauthorized();
        }

        HttpContext.Response.Cookies.Append(
            cookieName,
            result.Value!.RefreshToken,
            RefreshCookie.Options(options.Value));
        return TypedResults.Ok(new RefreshTokenResponse(
            result.Value.UserId,
            result.Value.UserName,
            IdentityProtocolMapper.ToProtocol(result.Value.Role),
            result.Value.EmailVerified,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
