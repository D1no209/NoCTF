using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using NoCTF.Application.Authentication.RefreshSession;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RefreshTokenEndpoint(RefreshAccessToken refresh)
    : EndpointWithoutRequest<LoginResponse>
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!HttpContext.Request.Cookies.TryGetValue("noctf_refresh", out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            await HttpContext.Response.SendUnauthorizedAsync(cancellationToken);
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var result = await refresh.ExecuteAsync(hash, DateTimeOffset.UtcNow, cancellationToken);
        if (!result.Succeeded)
        {
            HttpContext.Response.Cookies.Delete("noctf_refresh");
            await HttpContext.Response.SendUnauthorizedAsync(cancellationToken);
            return;
        }

        HttpContext.Response.Cookies.Append("noctf_refresh", result.Value!.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
        await HttpContext.Response.SendAsync<LoginResponse>(
            new(result.Value.UserId, result.Value.UserName, result.Value.Role, result.Value.AccessToken, result.Value.AccessTokenExpiresAt),
            StatusCodes.Status200OK,
            null,
            cancellationToken);
    }
}
