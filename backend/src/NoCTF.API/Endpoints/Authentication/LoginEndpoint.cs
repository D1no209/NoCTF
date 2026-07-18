using FastEndpoints;
using NoCTF.Application.Authentication.Login;
using Microsoft.AspNetCore.Http;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LoginEndpoint(LoginUser login) : Endpoint<LoginRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await login.ExecuteAsync(new(request.Login, request.Password), cancellationToken);
        if (!result.Succeeded)
        {
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
        await HttpContext.Response.SendAsync<LoginResponse>(new(
            result.Value!.UserId,
            result.Value.UserName,
            result.Value.Role,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt), StatusCodes.Status200OK, null, cancellationToken);
    }
}
