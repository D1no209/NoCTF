using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class RemoveCollaboratorEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : Endpoint<EmptyRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/competitions/{id}/collaborators/{userId}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("id");
        var userId = Route<Guid>("userId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var collaborator = await dbContext.CompetitionCollaborators
            .FirstOrDefaultAsync(cc => cc.CompetitionId == competitionId && cc.UserId == userId, ct);

        if (collaborator is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        dbContext.CompetitionCollaborators.Remove(collaborator);
        await dbContext.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
