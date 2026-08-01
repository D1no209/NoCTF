using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Login;
using NoCTF.Domain.Identity;
using Microsoft.AspNetCore.Http;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record LoginResponse(
    Guid UserId,
    string UserName,
    UserRole Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginValidator : Validator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(request => request.Login).NotEmpty().MaximumLength(320);
        RuleFor(request => request.Password).NotEmpty().MaximumLength(1024);
    }
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
        HttpContext.Response.Cookies.Append("__Secure-noctf_refresh", result.Value!.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            MaxAge = TimeSpan.FromDays(30)
        });
        return TypedResults.Ok(new LoginResponse(
            result.Value!.UserId,
            result.Value.UserName,
            result.Value.Role,
            result.Value.EmailVerified,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt));
    }
}
