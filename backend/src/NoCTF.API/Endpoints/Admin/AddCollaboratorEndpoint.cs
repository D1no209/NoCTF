using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class AddCollaboratorRequest
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Observer";
}

public class AddCollaboratorEndpoint(ApplicationDbContext dbContext) : Endpoint<AddCollaboratorRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/collaborators");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(AddCollaboratorRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("id");

        var exists = await dbContext.CompetitionCollaborators
            .AnyAsync(cc => cc.CompetitionId == competitionId && cc.UserId == req.UserId, ct);

        if (exists)
        {
            ThrowError("User is already a collaborator.");
        }

        if (!Enum.TryParse<CollaboratorRole>(req.Role, ignoreCase: true, out var role))
            role = CollaboratorRole.Observer;

        var collaborator = new CompetitionCollaborator
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            UserId = req.UserId,
            Role = role,
            AddedAt = DateTime.UtcNow,
        };

        dbContext.CompetitionCollaborators.Add(collaborator);
        await dbContext.SaveChangesAsync(ct);

        await SendCreatedAtAsync<GetCollaboratorsEndpoint>(new { id = competitionId }, null, cancellation: ct);
    }
}
