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

public class LoginEndpoint(
    ApplicationDbContext dbContext,
    JwtTokenService jwtService,
    IEmailVerificationService emailVerification) : Endpoint<LoginRequest, LoginResponse>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("auth-login"));
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var identifier = NormalizeIdentifier(req.Email);
        var user = await FindUserAsync(dbContext, identifier, ct);

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

        if (await emailVerification.IsEnabledAsync(ct) && !user.EmailVerifiedAt.HasValue)
        {
            await SendStringAsync("email_not_verified", StatusCodes.Status403Forbidden, cancellation: ct);
            return;
        }

        await SendAsync(new LoginResponse
        {
            AccessToken = jwtService.GenerateToken(user),
            UserName = user.UserName,
            Role = user.Role.ToString()
        }, cancellation: ct);
    }

    internal static string NormalizeIdentifier(string? identifier)
        => identifier?.Trim().ToLowerInvariant() ?? string.Empty;

    internal static Task<User?> FindUserAsync(
        ApplicationDbContext dbContext,
        string normalizedIdentifier,
        CancellationToken ct = default)
        => dbContext.Database.IsRelational()
            ? dbContext.Users.FirstOrDefaultAsync(
                user => EF.Property<string>(user, "NormalizedEmail") == normalizedIdentifier ||
                        EF.Property<string>(user, "NormalizedUserName") == normalizedIdentifier,
                ct)
            : dbContext.Users.FirstOrDefaultAsync(
                user => user.Email.ToLower() == normalizedIdentifier ||
                        user.UserName.ToLower() == normalizedIdentifier,
                ct);
}
