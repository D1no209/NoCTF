using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Auth;

public class RegisterRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
}

public class RegisterEndpoint(ApplicationDbContext dbContext) : Endpoint<RegisterRequest, RegisterResponse>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/auth/register");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("auth-register"));
    }

    public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
    {
        var userName = req.UserName.Trim();
        var email = req.Email.Trim().ToLowerInvariant();

        if (userName.Length < 3)
        {
            AddError(r => r.UserName, "User name must be at least 3 characters.");
        }

        if (passwordTooShort(req.Password))
        {
            AddError(r => r.Password, "Password must be at least 8 characters.");
        }

        if (!email.Contains('@'))
        {
            AddError(r => r.Email, "Email is invalid.");
        }

        if (ValidationFailed)
        {
            await SendErrorsAsync(400, ct);
            return;
        }

        var exists = await dbContext.Users.AnyAsync(
            u => u.Email.ToLower() == email || u.UserName.ToLower() == userName.ToLower(),
            ct);
        if (exists)
        {
            AddError("A user with the same email or user name already exists.");
            await SendErrorsAsync(409, ct);
            return;
        }

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            PasswordHash = hasher.HashPassword(null!, req.Password),
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(new RegisterResponse { Id = user.Id, UserName = user.UserName }, 201, ct);
    }

    private static bool passwordTooShort(string password) => password.Length < 8;
}
