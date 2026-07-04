using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DestroyContainerRequest
{
    public string Id { get; set; } = string.Empty;
}

public class DestroyContainerEndpoint(ApplicationDbContext db, IContainerManager containerManager, ICompetitionPermissionService permissions) : Endpoint<DestroyContainerRequest>, IAuditableEndpoint
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
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, box.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var instance = new ContainerInstance(
            Guid.NewGuid(),
            box.CompetitionId,
            box.TeamId,
            box.ChallengeId,
            box.ProviderType,
            req.Id,
            new Dictionary<int, int>(),
            "running",
            DateTime.UtcNow,
            OrchestrationNamespace: box.OrchestrationNamespace);

        await containerManager.DestroyContainerAsync(instance, ct);
        db.AwdGameBoxes.Remove(box);
        AuditLogWriter.Add(
            db,
            HttpContext,
            "container.instance.destroyed",
            "Container",
            req.Id,
            new
            {
                competitionId = box.CompetitionId,
                challengeId = box.ChallengeId,
                teamId = box.TeamId,
                containerId = req.Id,
                reason = "admin_destroy"
            });
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
