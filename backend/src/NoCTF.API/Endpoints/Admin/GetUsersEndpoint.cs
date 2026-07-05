using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.Core;

namespace NoCTF.API.Endpoints.Admin;

public class UserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class GetUsersEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest, List<UserDto>>
{
    public override void Configure()
    {
        Get("/api/admin/users");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(UserRole.Admin.ToString());
        var query = HttpContext.Request.Query["q"].ToString().Trim();
        var limit = int.TryParse(HttpContext.Request.Query["limit"].ToString(), out var requestedLimit)
            ? Math.Clamp(requestedLimit, 1, 200)
            : 100;

        var usersQuery = dbContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var queryLower = query.ToLowerInvariant();
            usersQuery = usersQuery.Where(u =>
                u.UserName.ToLower().Contains(queryLower) ||
                (isAdmin && u.Email.ToLower().Contains(queryLower)));
        }

        var users = await usersQuery
            .OrderBy(u => u.UserName)
            .Take(limit)
            .Select(u => new UserDto
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = isAdmin ? u.Email : string.Empty,
                Role = isAdmin ? u.Role.ToString().ToLowerInvariant() : string.Empty
            })
            .ToListAsync(ct);

        await SendAsync(users, cancellation: ct);
    }
}
