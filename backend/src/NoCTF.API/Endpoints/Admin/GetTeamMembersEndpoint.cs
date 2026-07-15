using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class TeamMembersDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class GetTeamMembersEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : Endpoint<EmptyRequest, List<TeamMembersDto>>
{
    public override void Configure()
    {
        Get("/api/admin/teams/{id}/members");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var team = await dbContext.Teams.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, team.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var members = await dbContext.TeamMembers
            .IgnoreQueryFilters()
            .Where(tm => tm.CompetitionId == team.CompetitionId && tm.TeamId == id)
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
