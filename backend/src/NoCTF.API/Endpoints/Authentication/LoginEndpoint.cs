using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Login;
using Microsoft.AspNetCore.Http;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginEndpoint(LoginUser login)
    : Endpoint<LoginRequest, Results<Ok<LoginResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await login.ExecuteAsync(new(request.Login, request.Password), cancellationToken);
        if (!result.Succeeded)
        {
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
            result.Value!.UserId,
            result.Value.UserName,
            result.Value.Role,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
