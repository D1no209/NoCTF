using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class ResetUserPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class ResetUserPasswordEndpoint(ApplicationDbContext dbContext) : Endpoint<ResetUserPasswordRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/users/{id}/reset-password");
        Roles("Admin");
    }

    public override async Task HandleAsync(ResetUserPasswordRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var user = await dbContext.Users.FindAsync([id], ct);
        if (user is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
        {
            ThrowError("Password must be at least 6 characters.");
        }

        var hasher = new PasswordHasher<object>();
        user.PasswordHash = hasher.HashPassword(null!, req.NewPassword);
        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        await SendOkAsync(ct);
    }
}
