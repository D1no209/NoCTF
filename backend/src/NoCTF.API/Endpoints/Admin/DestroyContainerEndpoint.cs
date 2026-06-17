using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DestroyContainerRequest
{
    public string Id { get; set; } = string.Empty;
}

public class DestroyContainerEndpoint(ApplicationDbContext db, IContainerManager containerManager) : Endpoint<DestroyContainerRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/containers/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(DestroyContainerRequest req, CancellationToken ct)
    {
        var box = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.ContainerInstanceId == req.Id, ct);

        if (box is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var instance = new ContainerInstance(
            Guid.NewGuid(),
            box.CompetitionId,
            box.TeamId,
            box.ChallengeId,
            "docker",
            req.Id,
            new Dictionary<int, int>(),
            "running",
            DateTime.UtcNow);

        await containerManager.DestroyContainerAsync(instance, ct);
        db.AwdGameBoxes.Remove(box);
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
