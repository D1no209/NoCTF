using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteChallengeRequest
{
    public Guid Id { get; set; }
}

public class DeleteChallengeEndpoint(ApplicationDbContext db) : Endpoint<DeleteChallengeRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/challenges/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(DeleteChallengeRequest req, CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        db.Challenges.Remove(challenge);
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
