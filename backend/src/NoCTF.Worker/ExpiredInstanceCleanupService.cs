using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Worker;

public class ExpiredInstanceCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredInstanceCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredInstancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Expired instance cleanup failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    internal async Task CleanupExpiredInstancesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var containerManager = scope.ServiceProvider.GetRequiredService<IContainerManager>();
        var executionLease = scope.ServiceProvider.GetRequiredService<ICompetitionExecutionLease>();
        var maintenanceServices = scope.ServiceProvider.GetServices<IInstanceMaintenanceService>();
        var now = DateTime.UtcNow;

        var candidateIds = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(box => box.ContainerInstanceId != null && box.ExpiresAt != null && box.ExpiresAt <= now)
            .Where(box => box.CleanupLockedUntil == null || box.CleanupLockedUntil < now)
            .OrderBy(box => box.ExpiresAt)
            .Take(20)
            .Select(box => new ExpiredInstanceCandidate(
                box.Id,
                box.CompetitionId,
                box.TeamId,
                box.ChallengeId))
            .ToListAsync(ct);

        foreach (var candidate in candidateIds)
        {
            await using var transitionLease = await executionLease.TryAcquireAsync(
                db,
                CompetitionExecutionLeaseKeys.ChallengeInstance(candidate.TeamId, candidate.ChallengeId),
                candidate.CompetitionId,
                ct);
            if (transitionLease is null)
                continue;

            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
            var operationCt = leaseCts.Token;
            var cleanupOwner = Guid.NewGuid().ToString("N");
            var box = await TryClaimAsync(db, candidate.Id, cleanupOwner, now, operationCt);
            if (box is null)
                continue;

            var containerId = box.ContainerInstanceId;
            if (containerId is null)
            {
                await ReleaseClaimAsync(db, box.Id, cleanupOwner, CancellationToken.None);
                db.ChangeTracker.Clear();
                continue;
            }

            try
            {
                if (string.Equals(box.RuntimeKind, "compose", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(box.ComposeProjectName))
                {
                    await containerManager.ComposeDownAsync(new ComposeDeployment(
                        Guid.NewGuid(),
                        box.CompetitionId,
                        box.TeamId,
                        box.ChallengeId,
                        box.ProviderType,
                        box.ComposeProjectName,
                        string.IsNullOrWhiteSpace(box.ComposeYaml)
                            ? "services:\n  cleanup:\n    image: scratch\n"
                            : box.ComposeYaml,
                        "running",
                        box.CreatedAt,
                        box.ExpiresAt,
                        box.PublicHost,
                        box.EntryUrl,
                        box.OrchestrationNamespace), operationCt);
                }
                else
                {
                    await containerManager.DestroyContainerAsync(new ContainerInstance(
                        Guid.NewGuid(),
                        box.CompetitionId,
                        box.TeamId,
                        box.ChallengeId,
                        box.ProviderType,
                        containerId,
                        ReadPorts(box.PortMappingsJson),
                        "running",
                        DateTime.UtcNow,
                        OrchestrationNamespace: box.OrchestrationNamespace), operationCt);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to destroy expired container {ContainerId}. The instance record will be preserved for retry.", containerId);
                await ReleaseClaimAsync(db, box.Id, cleanupOwner, CancellationToken.None);
                db.ChangeTracker.Clear();
                continue;
            }

            try
            {
                if (!await FinalizeClaimAndArtifactsAsync(db, box, cleanupOwner, containerId, operationCt))
                {
                    // Another transition replaced this row after the old runtime was
                    // selected. Never clear the replacement or its newly generated flag.
                    db.ChangeTracker.Clear();
                    await ReleaseClaimAsync(db, box.Id, cleanupOwner, CancellationToken.None);
                    db.ChangeTracker.Clear();
                    continue;
                }

                db.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to persist cleanup for expired container {ContainerId}. The instance record will be preserved for retry.",
                    containerId);
                db.ChangeTracker.Clear();
                await ReleaseClaimAsync(db, box.Id, cleanupOwner, CancellationToken.None);
                db.ChangeTracker.Clear();
                if (ex is OperationCanceledException && ct.IsCancellationRequested)
                    throw;
            }
        }

        foreach (var service in maintenanceServices)
        {
            try
            {
                var result = await service.MaintainAsync(ct);
                if (result.ExpiredInstances > 0 || result.SyncedInstances > 0 || result.FailedInstances > 0)
                {
                    logger.LogInformation(
                        "Instance maintenance {Name} completed. Expired={Expired}, Synced={Synced}, Failed={Failed}.",
                        service.Name,
                        result.ExpiredInstances,
                        result.SyncedInstances,
                        result.FailedInstances);
                }

                // Plugin maintenance services share this scope for discovery.
                // Detach their state after every invocation so a later plugin
                // cannot accidentally persist another plugin's tracked changes.
                db.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Instance maintenance service {Name} failed.", service.Name);
            }
        }
    }

    private static Dictionary<int, int> ReadPorts(string? portMappingsJson)
    {
        if (string.IsNullOrWhiteSpace(portMappingsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, int>>(portMappingsJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed record ExpiredInstanceCandidate(
        Guid Id,
        Guid CompetitionId,
        Guid TeamId,
        Guid ChallengeId);

    private static async Task<AwdGameBox?> TryClaimAsync(
        ApplicationDbContext db,
        Guid id,
        string owner,
        DateTime now,
        CancellationToken ct)
    {
        var lockedUntil = now.AddMinutes(5);
        if (db.Database.IsRelational())
        {
            var updated = await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .Where(box =>
                    box.Id == id &&
                    box.ContainerInstanceId != null &&
                    box.ExpiresAt != null &&
                    box.ExpiresAt <= now &&
                    (box.CleanupLockedUntil == null || box.CleanupLockedUntil < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(box => box.CleanupOwner, owner)
                    .SetProperty(box => box.CleanupLockedUntil, lockedUntil), ct);
            if (updated != 1)
                return null;

            return await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(box => box.Id == id && box.CleanupOwner == owner, ct);
        }

        var candidate = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(box =>
                box.Id == id &&
                box.ContainerInstanceId != null &&
                box.ExpiresAt != null &&
                box.ExpiresAt <= now &&
                (box.CleanupLockedUntil == null || box.CleanupLockedUntil < now), ct);
        if (candidate is null)
            return null;
        candidate.CleanupOwner = owner;
        candidate.CleanupLockedUntil = lockedUntil;
        await db.SaveChangesAsync(ct);
        return candidate;
    }

    private static async Task ReleaseClaimAsync(
        ApplicationDbContext db,
        Guid id,
        string owner,
        CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .Where(box => box.Id == id && box.CleanupOwner == owner)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(box => box.CleanupOwner, (string?)null)
                    .SetProperty(box => box.CleanupLockedUntil, (DateTime?)null), ct);
            return;
        }

        var box = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.CleanupOwner == owner, ct);
        if (box is null)
            return;
        box.CleanupOwner = null;
        box.CleanupLockedUntil = null;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> FinalizeClaimAsync(
        ApplicationDbContext db,
        AwdGameBox box,
        string owner,
        string containerId,
        CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            db.Entry(box).State = EntityState.Detached;
            var updated = await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .Where(candidate =>
                    candidate.Id == box.Id &&
                    candidate.CleanupOwner == owner &&
                    candidate.ContainerInstanceId == containerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.ContainerInstanceId, (string?)null)
                    .SetProperty(candidate => candidate.ProviderType, "docker")
                    .SetProperty(candidate => candidate.PublicHost, (string?)null)
                    .SetProperty(candidate => candidate.EntryUrl, (string?)null)
                    .SetProperty(candidate => candidate.OrchestrationNamespace, (string?)null)
                    .SetProperty(candidate => candidate.PortMappingsJson, "{}")
                    .SetProperty(candidate => candidate.RuntimeKind, "container")
                    .SetProperty(candidate => candidate.ComposeProjectName, (string?)null)
                    .SetProperty(candidate => candidate.ComposeYaml, (string?)null)
                    .SetProperty(candidate => candidate.InternalHost, (string?)null)
                    .SetProperty(candidate => candidate.InternalPortMappingsJson, "{}")
                    .SetProperty(candidate => candidate.ExpiresAt, (DateTime?)null)
                    .SetProperty(candidate => candidate.RuntimeOperationId, (Guid?)null)
                    .SetProperty(candidate => candidate.CleanupOwner, (string?)null)
                    .SetProperty(candidate => candidate.CleanupLockedUntil, (DateTime?)null), ct);
            return updated == 1;
        }

        if (box.CleanupOwner != owner || box.ContainerInstanceId != containerId)
            return false;
        box.ContainerInstanceId = null;
        box.ProviderType = "docker";
        box.PublicHost = null;
        box.EntryUrl = null;
        box.OrchestrationNamespace = null;
        box.PortMappingsJson = "{}";
        box.RuntimeKind = "container";
        box.ComposeProjectName = null;
        box.ComposeYaml = null;
        box.InternalHost = null;
        box.InternalPortMappingsJson = "{}";
        box.ExpiresAt = null;
        box.RuntimeOperationId = null;
        box.CleanupOwner = null;
        box.CleanupLockedUntil = null;
        return true;
    }

    private static async Task<bool> FinalizeClaimAndArtifactsAsync(
        ApplicationDbContext db,
        AwdGameBox box,
        string owner,
        string containerId,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            if (!await FinalizeClaimAsync(db, box, owner, containerId, ct))
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            var dynamicFlags = await db.CtfDynamicFlags
                .IgnoreQueryFilters()
                .Where(f =>
                    f.CompetitionId == box.CompetitionId &&
                    f.TeamId == box.TeamId &&
                    f.ChallengeId == box.ChallengeId)
                .ToListAsync(ct);
            db.CtfDynamicFlags.RemoveRange(dynamicFlags);

            db.CompetitionLogs.Add(new CompetitionLog
            {
                Id = Guid.NewGuid(),
                CompetitionId = box.CompetitionId,
                Level = "info",
                EventType = "container.destroyed",
                Message = "Dynamic container expired and was destroyed.",
                TeamId = box.TeamId,
                ChallengeId = box.ChallengeId,
                MetadataJson = JsonSerializer.Serialize(new { containerId, reason = "expired" }, JsonOptions),
                CreatedAt = DateTime.UtcNow,
            });

            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserName = "system",
                Action = "container.instance.destroyed",
                EntityType = "Container",
                EntityId = containerId,
                EndpointPath = "system",
                HttpMethod = "SYSTEM",
                NewValues = JsonSerializer.Serialize(new
                {
                    competitionId = box.CompetitionId,
                    challengeId = box.ChallengeId,
                    teamId = box.TeamId,
                    containerId,
                    reason = "expired"
                }, JsonOptions),
                Timestamp = DateTime.UtcNow,
            });

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
