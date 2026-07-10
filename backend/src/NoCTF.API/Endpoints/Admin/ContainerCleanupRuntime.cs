using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

internal static class ContainerCleanupRuntime
{
    public static async Task CleanupCompetitionAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        Guid competitionId,
        HttpContext? httpContext,
        Guid? userId,
        string reason,
        CancellationToken ct)
        => await CleanupScopeAsync(db, containerManager, competitionId, null, httpContext, userId, reason, ct);

    public static async Task CleanupTeamAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        Guid competitionId,
        Guid teamId,
        HttpContext? httpContext,
        Guid? userId,
        string reason,
        CancellationToken ct)
        => await CleanupScopeAsync(db, containerManager, competitionId, teamId, httpContext, userId, reason, ct);

    private static async Task CleanupScopeAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        Guid competitionId,
        Guid? teamId,
        HttpContext? httpContext,
        Guid? userId,
        string reason,
        CancellationToken ct)
    {
        var cleanupFailures = new List<Exception>();
        var boxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId && (!teamId.HasValue || g.TeamId == teamId.Value))
            .ToListAsync(ct);
        foreach (var box in boxes.Where(box =>
                     !string.IsNullOrWhiteSpace(box.ContainerInstanceId) ||
                     !string.IsNullOrWhiteSpace(box.ComposeProjectName)))
        {
            try
            {
                await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
                    db,
                    containerManager,
                    box,
                    httpContext,
                    reason,
                    userId,
                    updateCooldown: false,
                    ct);
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
                AddCleanupFailure(db, httpContext, "container.cleanup_failed", box.Id, reason, ex);
            }
        }

        var teamInstances = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && (!teamId.HasValue || i.TeamId == teamId.Value))
            .ToListAsync(ct);
        foreach (var instance in teamInstances.Where(i => !string.IsNullOrWhiteSpace(i.ComposeProjectName)))
        {
            try
            {
                await containerManager.ComposeDownAsync(new ComposeDeployment(
                    Id: Guid.NewGuid(),
                    CompetitionId: instance.CompetitionId,
                    TeamId: instance.TeamId,
                    ChallengeId: instance.ChallengeId,
                    ProviderType: "compose",
                    ProjectName: instance.ComposeProjectName!,
                    ComposeYaml: string.IsNullOrWhiteSpace(instance.RenderedComposeYaml)
                        ? "services:\n  cleanup:\n    image: scratch\n"
                        : instance.RenderedComposeYaml,
                    Status: instance.Status.ToString().ToLowerInvariant(),
                    StartedAt: instance.CreatedAt,
                    ExpectedStopAt: instance.ExpiresAt), ct);
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
                AddCleanupFailure(db, httpContext, "compose.cleanup_failed", instance.Id, reason, ex);
            }
        }

        if (cleanupFailures.Count > 0)
            throw new InvalidOperationException(
                "container_cleanup_failed: runtime resources were not fully destroyed; database state was preserved.",
                cleanupFailures[0]);

        db.AwdGameBoxes.RemoveRange(boxes);
        db.TeamChallengeInstances.RemoveRange(teamInstances);

        var ctfDynamicFlags = await db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && (!teamId.HasValue || f.TeamId == teamId.Value))
            .ToListAsync(ct);
        db.CtfDynamicFlags.RemoveRange(ctfDynamicFlags);

        var penetrationDynamicFlags = await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && (!teamId.HasValue || f.TeamId == teamId.Value))
            .ToListAsync(ct);
        db.DynamicFlagInstances.RemoveRange(penetrationDynamicFlags);
    }

    private static void AddCleanupFailure(
        ApplicationDbContext db,
        HttpContext? httpContext,
        string action,
        Guid entityId,
        string reason,
        Exception ex)
    {
        var values = new { reason, error = ex.Message };
        if (httpContext is not null)
        {
            AuditLogWriter.Add(
                db,
                httpContext,
                action,
                "Container",
                entityId.ToString(),
                newValues: values,
                exception: ex.Message);
            return;
        }

        AuditLogWriter.AddSystem(
            db,
            action,
            "Container",
            entityId.ToString(),
            newValues: values,
            exception: ex.Message);
    }
}
