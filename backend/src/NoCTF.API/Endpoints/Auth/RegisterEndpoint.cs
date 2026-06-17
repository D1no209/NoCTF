using FastEndpoints;
using Microsoft.AspNetCore.Identity;
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
    }

    public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
    {
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = req.UserName,
            Email = req.Email,
            PasswordHash = hasher.HashPassword(null!, req.Password),
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(new RegisterResponse { Id = user.Id, UserName = user.UserName }, 201, ct);
    }
}
