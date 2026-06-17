using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class TeamMembersDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class GetTeamMembersEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest, List<TeamMembersDto>>
{
    public override void Configure()
    {
        Get("/api/admin/teams/{id}/members");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var members = await dbContext.TeamMembers
            .Where(tm => tm.TeamId == id)
            .Select(tm => new TeamMembersDto
            {
                UserId = tm.UserId,
                UserName = dbContext.Users.Where(u => u.Id == tm.UserId).Select(u => u.UserName).FirstOrDefault() ?? string.Empty,
                Role = tm.Role.ToString().ToLowerInvariant(),
            })
            .ToListAsync(ct);

        await SendAsync(members, cancellation: ct);
    }
}
