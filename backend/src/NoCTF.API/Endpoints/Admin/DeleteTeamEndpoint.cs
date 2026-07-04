using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteTeamEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : Endpoint<EmptyRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/teams/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var team = await dbContext.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, ct);
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

        // Remove team members first
        var members = await dbContext.TeamMembers.Where(tm => tm.TeamId == id).ToListAsync(ct);
        dbContext.TeamMembers.RemoveRange(members);
        dbContext.Teams.Remove(team);
        await dbContext.SaveChangesAsync(ct);

        await SendNoContentAsync(ct);
    }
}
