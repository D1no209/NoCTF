using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Auth;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Auth;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class LoginEndpoint(ApplicationDbContext dbContext, JwtTokenService jwtService) : Endpoint<LoginRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == req.Email, ct);

        if (user is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var hasher = new PasswordHasher<User>();
        var result = hasher.VerifyHashedPassword(null!, user.PasswordHash, req.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await SendAsync(new LoginResponse
        {
            AccessToken = jwtService.GenerateToken(user),
            UserName = user.UserName,
            Role = user.Role.ToString().ToLowerInvariant()
        }, cancellation: ct);
    }
}
