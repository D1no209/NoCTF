using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using System.Text.Json;

namespace NoCTF.API.Endpoints.Admin;

internal static class ContainerCleanupRuntime
{
    public static async Task CleanupCompetitionAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        ICompetitionExecutionLease executionLease,
        Guid competitionId,
        HttpContext? httpContext,
        Guid? userId,
        string reason,
        CancellationToken ct)
        => await CleanupScopeAsync(db, containerManager, executionLease, competitionId, null, httpContext, userId, reason, ct);

    public static async Task CleanupTeamAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        ICompetitionExecutionLease executionLease,
        Guid competitionId,
        Guid teamId,
        HttpContext? httpContext,
        Guid? userId,
        string reason,
        CancellationToken ct)
        => await CleanupScopeAsync(db, containerManager, executionLease, competitionId, teamId, httpContext, userId, reason, ct);

    private static async Task CleanupScopeAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        ICompetitionExecutionLease executionLease,
        Guid competitionId,
        Guid? teamId,
        HttpContext? httpContext,
        Guid? userId,
        string reason,
        CancellationToken ct)
    {
        var cleanupFailures = new List<Exception>();
        var boxCandidates = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(g => g.CompetitionId == competitionId && (!teamId.HasValue || g.TeamId == teamId.Value))
            .Select(g => new { g.Id, g.TeamId, g.ChallengeId })
            .ToListAsync(ct);
        var boxes = new List<AwdGameBox>(boxCandidates.Count);
        foreach (var candidate in boxCandidates)
        {
            try
            {
                await using var lease = await executionLease.TryAcquireAsync(
                    db,
                    CompetitionExecutionLeaseKeys.ChallengeInstance(candidate.TeamId, candidate.ChallengeId),
                    competitionId,
                    ct) ?? throw new InvalidOperationException("instance_transition_in_progress");
                using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
                var box = await db.AwdGameBoxes
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(g => g.Id == candidate.Id, leaseCts.Token);
                if (box is null)
                    continue;

                boxes.Add(box);
                if (string.IsNullOrWhiteSpace(box.ContainerInstanceId) &&
                    string.IsNullOrWhiteSpace(box.ComposeProjectName))
                {
                    continue;
                }

                await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
                    db,
                    containerManager,
                    box,
                    httpContext,
                    reason,
                    userId,
                    updateCooldown: false,
                    leaseCts.Token);
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
                AddCleanupFailure(db, httpContext, "container.cleanup_failed", candidate.Id, reason, ex);
            }
        }

        var instanceCandidates = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.CompetitionId == competitionId && (!teamId.HasValue || i.TeamId == teamId.Value))
            .Select(i => new { i.Id, i.TeamId, i.ChallengeId })
            .ToListAsync(ct);
        var teamInstances = new List<TeamChallengeInstance>(instanceCandidates.Count);
        foreach (var candidate in instanceCandidates)
        {
            try
            {
                await using var lease = await executionLease.TryAcquireAsync(
                    db,
                    CompetitionExecutionLeaseKeys.ChallengeInstance(candidate.TeamId, candidate.ChallengeId),
                    competitionId,
                    ct) ?? throw new InvalidOperationException("instance_transition_in_progress");
                using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
                var instance = await db.TeamChallengeInstances
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(i => i.Id == candidate.Id, leaseCts.Token);
                if (instance is null)
                    continue;

                teamInstances.Add(instance);
                if (string.IsNullOrWhiteSpace(instance.ComposeProjectName))
                    continue;

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
                    ExpectedStopAt: instance.ExpiresAt), leaseCts.Token);
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
                AddCleanupFailure(db, httpContext, "compose.cleanup_failed", candidate.Id, reason, ex);
            }
        }

        try
        {
            await CleanupDetachedAwdpContainersAsync(
                db,
                containerManager,
                competitionId,
                teamId,
                challengeId: null,
                ct);
        }
        catch (Exception ex)
        {
            cleanupFailures.Add(ex);
            AddCleanupFailure(db, httpContext, "awdp.candidate_cleanup_failed", competitionId, reason, ex);
        }

        if (cleanupFailures.Count > 0)
        {
            await PersistCleanupFailureAuditAsync(db);
            throw new InvalidOperationException(
                "container_cleanup_failed: runtime resources were not fully destroyed; database state was preserved.",
                cleanupFailures[0]);
        }

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

    internal static async Task CleanupDetachedAwdpContainersAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        Guid competitionId,
        Guid? teamId,
        Guid? challengeId,
        CancellationToken ct)
    {
        var tasks = await db.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(task =>
                task.CompetitionId == competitionId &&
                task.Type == "awdp.container.cleanup" &&
                task.Status != BackgroundTaskStatus.Succeeded &&
                task.Status != BackgroundTaskStatus.Cancelled)
            .ToListAsync(ct);
        var failures = new List<Exception>();
        foreach (var task in tasks)
        {
            AwdpDetachedCleanupPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AwdpDetachedCleanupPayload>(
                    task.PayloadJson,
                    AwdpCleanupJsonOptions);
            }
            catch (JsonException ex)
            {
                failures.Add(ex);
                continue;
            }

            if (payload?.Container is null ||
                (teamId.HasValue && payload.Container.TeamId != teamId) ||
                (challengeId.HasValue && payload.Container.ChallengeId != challengeId))
            {
                continue;
            }

            try
            {
                await containerManager.DestroyContainerAsync(payload.Container, ct);
                task.Status = BackgroundTaskStatus.Succeeded;
                task.LockOwner = null;
                task.LockedUntil = null;
                task.LastError = null;
                task.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures.Add(ex);
            }
        }

        if (tasks.Count > 0)
            await db.SaveChangesAsync(ct);
        if (failures.Count > 0)
            throw new InvalidOperationException(
                "awdp_candidate_cleanup_failed",
                failures[0]);
    }

    private static async Task PersistCleanupFailureAuditAsync(ApplicationDbContext db)
    {
        var auditEntries = db.ChangeTracker
            .Entries<AuditLog>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();
        db.ChangeTracker.Clear();
        if (auditEntries.Count == 0)
            return;

        db.AuditLogs.AddRange(auditEntries);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static readonly JsonSerializerOptions AwdpCleanupJsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record AwdpDetachedCleanupPayload(ContainerInstance Container);

    private static void AddCleanupFailure(
        ApplicationDbContext db,
        HttpContext? httpContext,
        string action,
        Guid entityId,
        string reason,
        Exception ex)
    {
        var safeError = $"container_cleanup_failed:{ex.GetType().Name}";
        var values = new { reason, error = safeError };
        if (httpContext is not null)
        {
            AuditLogWriter.Add(
                db,
                httpContext,
                action,
                "Container",
                entityId.ToString(),
                newValues: values,
                exception: safeError);
            return;
        }

        AuditLogWriter.AddSystem(
            db,
            action,
            "Container",
            entityId.ToString(),
            newValues: values,
            exception: safeError);
    }
}
