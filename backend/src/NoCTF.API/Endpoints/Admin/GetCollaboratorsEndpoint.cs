using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class CollaboratorDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class GetCollaboratorsEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest, List<CollaboratorDto>>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/collaborators");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var collaborators = await dbContext.CompetitionCollaborators
            .Where(cc => cc.CompetitionId == id)
            .Select(cc => new CollaboratorDto
            {
                UserId = cc.UserId,
                UserName = dbContext.Users.Where(u => u.Id == cc.UserId).Select(u => u.UserName).FirstOrDefault() ?? string.Empty,
                Role = cc.Role.ToString().ToLowerInvariant(),
            })
            .ToListAsync(ct);

        await SendAsync(collaborators, cancellation: ct);
    }
}
