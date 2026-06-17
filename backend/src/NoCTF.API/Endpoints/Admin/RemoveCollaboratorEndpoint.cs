using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class RemoveCollaboratorEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest>, IAuditableEndpoint
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
