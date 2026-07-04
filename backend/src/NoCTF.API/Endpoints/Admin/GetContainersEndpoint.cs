using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class ContainerDto
{
    public string ContainerId { get; set; } = string.Empty;
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string Status { get; set; } = "running";
}

public class GetContainersEndpoint(ApplicationDbContext db, ICompetitionPermissionService permissions) : Endpoint<EmptyRequest, List<ContainerDto>>
{
    public override void Configure()
    {
        Get("/api/admin/containers");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        if (!AdminCompetitionAuthorization.TryGetUserId(HttpContext, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var manageableCompetitionIds = await permissions.GetManageableCompetitionIdsAsync(userId, ct);
        var containers = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.ContainerInstanceId != null && manageableCompetitionIds.Contains(g.CompetitionId))
            .Select(g => new ContainerDto
            {
                ContainerId = g.ContainerInstanceId ?? "unknown",
                CompetitionId = g.CompetitionId,
                TeamId = g.TeamId,
                ChallengeId = g.ChallengeId,
                Status = "running",
            })
            .ToListAsync(ct);

        await SendAsync(containers, cancellation: ct);
    }
}
