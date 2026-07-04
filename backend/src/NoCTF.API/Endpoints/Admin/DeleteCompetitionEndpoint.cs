using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteCompetitionEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : Endpoint<EmptyRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/competitions/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var competition = await dbContext.Competitions.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        dbContext.Competitions.Remove(competition);
        await dbContext.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
