using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Auth;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class UpdateUserRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

public class UpdateUserRoleEndpoint(
    ApplicationDbContext dbContext,
    IUserTokenVersionCache tokenVersionCache) : Endpoint<UpdateUserRoleRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/users/{id}/role");
        Roles("Admin");
    }

    public override async Task HandleAsync(UpdateUserRoleRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var user = await dbContext.Users.FindAsync([id], ct);
        if (user is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (!Enum.TryParse<UserRole>(req.Role, ignoreCase: true, out var newRole))
        {
            ThrowError("Invalid role value.");
        }

        // Prevent promoting to Admin via this endpoint
        if (newRole == UserRole.Admin)
        {
            ThrowError("Cannot promote to Admin via this endpoint.");
        }

        user.Role = newRole;
        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        await tokenVersionCache.SetAsync(user.Id, user.TokenVersion);

        await SendOkAsync(ct);
    }
}
