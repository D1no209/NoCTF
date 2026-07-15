using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.Penetration;

public class PenetrationInstanceMaintenanceService(
    ApplicationDbContext db,
    IContainerManager containerManager,
    IConfiguration configuration,
    ILogger<PenetrationInstanceMaintenanceService> logger,
    ICompetitionExecutionLease? executionLease = null) : IInstanceMaintenanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int ExpiredBatchSize = 20;
    private const int SyncBatchSize = 50;
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromMinutes(10);
    private static readonly PenetrationInstanceStatus[] BusyStatuses =
    [
        PenetrationInstanceStatus.Starting,
        PenetrationInstanceStatus.Stopping,
        PenetrationInstanceStatus.Resetting,
        PenetrationInstanceStatus.Destroying
    ];

    public string Name => "penetration";

    public async Task<InstanceMaintenanceResult> MaintainAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i =>
                i.ExpiresAt != null &&
                i.ExpiresAt <= now &&
                i.Status != PenetrationInstanceStatus.Expired &&
                i.Status != PenetrationInstanceStatus.Destroyed)
            .OrderBy(i => i.ExpiresAt)
            .Take(ExpiredBatchSize)
            .ToListAsync(ct);

        var expiredCount = 0;
        var failedCount = 0;
        foreach (var instance in expired)
        {
            await using var lease = await TryAcquireTransitionLeaseAsync(instance, ct);
            if (lease is null)
                continue;
            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
            var leaseCt = leaseCts.Token;
            await db.Entry(instance).ReloadAsync(leaseCt);
            if (db.Entry(instance).State == EntityState.Detached)
                continue;
            if (instance.ExpiresAt is null || instance.ExpiresAt > DateTime.UtcNow ||
                instance.Status is PenetrationInstanceStatus.Expired or PenetrationInstanceStatus.Destroyed)
            {
                continue;
            }

            if (await ExpireAsync(instance, leaseCt))
                expiredCount++;
            else
                failedCount++;
            await db.SaveChangesAsync(leaseCt);
        }

        var syncCandidates = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i =>
                i.Status == PenetrationInstanceStatus.Running &&
                i.ComposeProjectName != null &&
                i.ExpiresAt != null &&
                i.ExpiresAt > now)
            .OrderBy(i => i.UpdatedAt)
            .Take(SyncBatchSize)
            .ToListAsync(ct);
        var syncMetadata = await LoadSyncMetadataAsync(syncCandidates, ct);

        var syncedCount = 0;
        foreach (var instance in syncCandidates)
        {
            await using var lease = await TryAcquireTransitionLeaseAsync(instance, ct);
            if (lease is null)
                continue;
            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
            var leaseCt = leaseCts.Token;
            await db.Entry(instance).ReloadAsync(leaseCt);
            if (db.Entry(instance).State == EntityState.Detached)
                continue;
            if (instance.Status != PenetrationInstanceStatus.Running ||
                instance.ExpiresAt is null || instance.ExpiresAt <= DateTime.UtcNow ||
                string.IsNullOrWhiteSpace(instance.ComposeProjectName))
            {
                continue;
            }

            syncMetadata.TryGetValue(instance.Id, out var metadata);
            if (metadata?.TopologyId != instance.TopologyId)
                metadata = null;
            if (await SyncStatusAsync(instance, metadata, leaseCt))
                syncedCount++;
            else
                failedCount++;
            await db.SaveChangesAsync(leaseCt);
        }

        var stuckBefore = now.Subtract(BusyTimeout);
        var stuck = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i =>
                BusyStatuses.Contains(i.Status) &&
                (i.LastActionAt == null || i.LastActionAt <= stuckBefore) &&
                i.UpdatedAt <= stuckBefore)
            .OrderBy(i => i.UpdatedAt)
            .Take(ExpiredBatchSize)
            .ToListAsync(ct);

        foreach (var instance in stuck)
        {
            await using var lease = await TryAcquireTransitionLeaseAsync(instance, ct);
            if (lease is null)
                continue;
            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
            var leaseCt = leaseCts.Token;
            await db.Entry(instance).ReloadAsync(leaseCt);
            if (db.Entry(instance).State == EntityState.Detached)
                continue;
            var currentStuckBefore = DateTime.UtcNow.Subtract(BusyTimeout);
            if (!BusyStatuses.Contains(instance.Status) ||
                (instance.LastActionAt is not null && instance.LastActionAt > currentStuckBefore) ||
                instance.UpdatedAt > currentStuckBefore)
            {
                continue;
            }

            await MarkStuckInstanceFailedAsync(instance, leaseCt);
            failedCount++;
            await db.SaveChangesAsync(leaseCt);
        }

        return new InstanceMaintenanceResult(expiredCount, syncedCount, failedCount);
    }

    private Task<IExecutionLease?> TryAcquireTransitionLeaseAsync(
        TeamChallengeInstance instance,
        CancellationToken ct)
        => (executionLease ?? new CompetitionExecutionLease()).TryAcquireAsync(
            db,
            $"penetration-instance:{instance.TeamId:N}:{instance.ChallengeId:N}",
            instance.CompetitionId,
            ct);

    private async Task<bool> ExpireAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        try
        {
            await DownBestEffortAsync(instance, ct);
            await DeactivateDynamicFlagsAsync(instance, ct);

            var now = DateTime.UtcNow;
            instance.Status = PenetrationInstanceStatus.Expired;
            instance.RuntimeOperationId = null;
            instance.EntryPort = null;
            instance.EntryUrl = null;
            instance.ContainerIdsJson = "[]";
            instance.PortMappingsJson = "{}";
            instance.UpdatedAt = now;
            instance.LastError = null;

            AddCompetitionLog(
                instance.CompetitionId,
                "penetration.instance.expired",
                "Penetration range instance expired and was destroyed.",
                teamId: instance.TeamId,
                challengeId: instance.ChallengeId,
                metadata: new { instanceId = instance.Id, reason = "expired" });

            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserName = "system",
                Action = "penetration.instance.expired",
                EntityType = "PenetrationInstance",
                EntityId = instance.Id.ToString(),
                EndpointPath = "system",
                HttpMethod = "SYSTEM",
                NewValues = JsonSerializer.Serialize(new
                {
                    competitionId = instance.CompetitionId,
                    challengeId = instance.ChallengeId,
                    teamId = instance.TeamId,
                    instanceId = instance.Id,
                    reason = "expired"
                }, JsonOptions),
                Timestamp = now
            });

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to expire penetration instance {InstanceId}.", instance.Id);
            instance.Status = PenetrationInstanceStatus.Failed;
            instance.LastError = "instance_expiration_failed";
            instance.UpdatedAt = DateTime.UtcNow;
            return false;
        }
    }

    private async Task<bool> SyncStatusAsync(
        TeamChallengeInstance instance,
        SyncMetadata? metadata,
        CancellationToken ct)
    {
        try
        {
            var status = await containerManager.GetComposeStatusAsync(
                instance.ComposeProjectName!,
                BuildLabels(instance),
                ct);

            var containerIds = status.Services
                .Select(s => s.ContainerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            instance.ContainerIdsJson = JsonSerializer.Serialize(containerIds, JsonOptions);

            if (string.Equals(status.Status, "not_found", StringComparison.OrdinalIgnoreCase))
            {
                await DeactivateDynamicFlagsAsync(instance, ct);
                instance.Status = PenetrationInstanceStatus.Failed;
                instance.RuntimeOperationId = null;
                instance.EntryPort = null;
                instance.EntryUrl = null;
                instance.PortMappingsJson = "{}";
                instance.LastError = "Compose project was not found during status sync.";
                instance.UpdatedAt = DateTime.UtcNow;
                return true;
            }

            if (string.Equals(status.Status, "stopped", StringComparison.OrdinalIgnoreCase))
            {
                await DeactivateDynamicFlagsAsync(instance, ct);
                instance.Status = PenetrationInstanceStatus.Stopped;
                instance.RuntimeOperationId = null;
                instance.EntryPort = null;
                instance.EntryUrl = null;
                instance.PortMappingsJson = "{}";
                instance.LastError = null;
                instance.UpdatedAt = DateTime.UtcNow;
                return true;
            }

            if (metadata is null)
                return true;

            var entryNode = metadata.EntryNode;
            if (entryNode is not null)
            {
                var entryContainerPort = PenetrationComposeBuilder.GetPrimaryContainerPort(
                    entryNode,
                    metadata.ExposedPort);
                var entryService = status.Services.FirstOrDefault(s => s.NodeId == entryNode.Id);
                var hostPort = entryService?.PublishedPorts.GetValueOrDefault(entryContainerPort) ?? 0;
                if (hostPort > 0)
                {
                    var resolvedHost = !string.IsNullOrWhiteSpace(entryService?.PublicHost)
                        ? entryService.PublicHost
                        : ResolveAccessHost();
                    if (!string.IsNullOrWhiteSpace(resolvedHost))
                        instance.EntryHost = resolvedHost;

                    instance.EntryPort = hostPort;
                    var resolvedEntryUrl = !string.IsNullOrWhiteSpace(entryService?.EntryUrl)
                        ? entryService.EntryUrl
                        : !string.IsNullOrWhiteSpace(resolvedHost)
                            ? BuildEntryUrl(resolvedHost, hostPort, metadata.EntryConfigJson)
                            : null;
                    if (!string.IsNullOrWhiteSpace(resolvedEntryUrl))
                        instance.EntryUrl = resolvedEntryUrl;

                    instance.PortMappingsJson = JsonSerializer.Serialize(new Dictionary<int, int> { [entryContainerPort] = hostPort }, JsonOptions);
                }
                else
                {
                    // The runtime snapshot is authoritative: once the entry
                    // service loses its published port, never expose the prior
                    // port/URL which may already have been reassigned.
                    ClearEntryEndpoint(instance);
                }
            }
            else
            {
                ClearEntryEndpoint(instance);
            }

            instance.Status = PenetrationInstanceStatus.Running;
            instance.LastError = null;
            instance.UpdatedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync penetration instance {InstanceId}.", instance.Id);
            return false;
        }
    }

    private async Task MarkStuckInstanceFailedAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        await DeactivateDynamicFlagsAsync(instance, ct);
        var previousStatus = instance.Status.ToString();
        var cleanupFailed = false;
        try
        {
            await DownBestEffortAsync(instance, ct);
        }
        catch (Exception)
        {
            cleanupFailed = true;
        }

        instance.Status = PenetrationInstanceStatus.Failed;
        if (!cleanupFailed)
            instance.RuntimeOperationId = null;
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.ContainerIdsJson = "[]";
        instance.PortMappingsJson = "{}";
        instance.LastError = !cleanupFailed
            ? $"Instance maintenance timed out while {previousStatus}."
            : $"Instance maintenance timed out while {previousStatus}; cleanup failed.";
        instance.UpdatedAt = DateTime.UtcNow;

        AddCompetitionLog(
            instance.CompetitionId,
            "penetration.instance.maintenance_timeout",
            "Penetration range instance maintenance timed out.",
            "warning",
            instance.TeamId,
            instance.ChallengeId,
            new { instanceId = instance.Id, previousStatus });
    }

    private async Task DownBestEffortAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instance.ComposeProjectName))
            return;

        try
        {
            await containerManager.ComposeDownAsync(new ComposeDeployment(
                Id: Guid.NewGuid(),
                CompetitionId: instance.CompetitionId,
                TeamId: instance.TeamId,
                ChallengeId: instance.ChallengeId,
                ProviderType: "docker-compose",
                ProjectName: instance.ComposeProjectName,
                ComposeYaml: string.IsNullOrWhiteSpace(instance.RenderedComposeYaml)
                    ? "services:\n  cleanup:\n    image: scratch\n"
                    : instance.RenderedComposeYaml,
                Status: instance.Status.ToString(),
                StartedAt: instance.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Compose down failed for penetration instance {InstanceId}. Runtime state is preserved for retry.", instance.Id);
            instance.LastError = "instance_cleanup_failed";
            instance.UpdatedAt = DateTime.UtcNow;
            throw;
        }
    }

    private async Task DeactivateDynamicFlagsAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        var activeFlags = db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == instance.CompetitionId &&
                f.TeamId == instance.TeamId &&
                f.ChallengeId == instance.ChallengeId &&
                f.InstanceId == instance.Id &&
                f.IsActive);
        if (db.Database.IsRelational())
        {
            await activeFlags.ExecuteUpdateAsync(
                setters => setters.SetProperty(flag => flag.IsActive, false),
                ct);
            foreach (var entry in db.ChangeTracker.Entries<DynamicFlagInstance>().Where(entry =>
                         entry.Entity.InstanceId == instance.Id && entry.Entity.IsActive))
            {
                entry.Entity.IsActive = false;
            }
            return;
        }

        var flags = await activeFlags.ToListAsync(ct);
        foreach (var flag in flags)
            flag.IsActive = false;
    }

    private async Task<Dictionary<Guid, SyncMetadata>> LoadSyncMetadataAsync(
        IReadOnlyCollection<TeamChallengeInstance> instances,
        CancellationToken ct)
    {
        if (instances.Count == 0)
            return [];

        var competitionIds = instances.Select(instance => instance.CompetitionId).Distinct().ToArray();
        var challengeIds = instances.Select(instance => instance.ChallengeId).Distinct().ToArray();
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(challenge =>
                competitionIds.Contains(challenge.CompetitionId) &&
                challengeIds.Contains(challenge.Id) &&
                !challenge.IsDeleting)
            .Select(challenge => new
            {
                challenge.CompetitionId,
                challenge.Id,
                challenge.ExposedPort
            })
            .ToListAsync(ct);
        var challengeMap = challenges.ToDictionary(
            challenge => (challenge.CompetitionId, challenge.Id));

        var topologyIds = instances
            .Where(instance => instance.TopologyId.HasValue)
            .Select(instance => instance.TopologyId!.Value)
            .Distinct()
            .ToArray();
        var entryNodes = await db.PenetrationNodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(node =>
                competitionIds.Contains(node.CompetitionId) &&
                topologyIds.Contains(node.TopologyId) &&
                node.IsEntry)
            .OrderBy(node => node.DisplayOrder)
            .ToListAsync(ct);
        var entryNodeMap = entryNodes
            .GroupBy(node => (node.CompetitionId, node.TopologyId))
            .ToDictionary(group => group.Key, group => group.First());
        var topologies = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(topology =>
                competitionIds.Contains(topology.CompetitionId) &&
                topologyIds.Contains(topology.Id))
            .Select(topology => new
            {
                topology.CompetitionId,
                topology.Id,
                topology.EntryConfigJson
            })
            .ToListAsync(ct);
        var topologyMap = topologies.ToDictionary(
            topology => (topology.CompetitionId, topology.Id));

        var result = new Dictionary<Guid, SyncMetadata>(instances.Count);
        foreach (var instance in instances)
        {
            if (!challengeMap.TryGetValue(
                    (instance.CompetitionId, instance.ChallengeId),
                    out var challenge))
            {
                continue;
            }

            PenetrationNode? entryNode = null;
            var entryConfigJson = "{}";
            if (instance.TopologyId is { } topologyId)
            {
                entryNodeMap.TryGetValue((instance.CompetitionId, topologyId), out entryNode);
                if (topologyMap.TryGetValue((instance.CompetitionId, topologyId), out var topology))
                    entryConfigJson = topology.EntryConfigJson;
            }

            result[instance.Id] = new SyncMetadata(
                instance.TopologyId,
                entryNode,
                challenge.ExposedPort,
                entryConfigJson);
        }

        return result;
    }

    private string? ResolveAccessHost()
    {
        var configured = configuration["InstanceAccess:PublicHost"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (Uri.TryCreate(configuration["App:PublicBaseUrl"], UriKind.Absolute, out var appBaseUrl) &&
            !string.IsNullOrWhiteSpace(appBaseUrl.Host))
            return appBaseUrl.Host;

        return string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : null;
    }

    private static string? BuildEntryUrl(string? host, int? port, string entryConfigJson)
    {
        if (string.IsNullOrWhiteSpace(host) || port is null or <= 0) return null;
        var scheme = ReadEntryScheme(entryConfigJson);
        var formattedHost = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
        return scheme is "http" or "https"
            ? $"{scheme}://{formattedHost}:{port}"
            : $"{formattedHost}:{port}";
    }

    private static string ReadEntryScheme(string entryConfigJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(entryConfigJson) ? "{}" : entryConfigJson);
            return doc.RootElement.TryGetProperty("scheme", out var scheme) && scheme.ValueKind == JsonValueKind.String
                ? scheme.GetString()?.Trim().ToLowerInvariant() ?? "tcp"
                : "tcp";
        }
        catch
        {
            return "tcp";
        }
    }

    private static void ClearEntryEndpoint(TeamChallengeInstance instance)
    {
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.PortMappingsJson = "{}";
    }

    private static Dictionary<string, string> BuildLabels(TeamChallengeInstance instance)
        => new()
        {
            ["noctf.kind"] = "penetration",
            ["competitionId"] = instance.CompetitionId.ToString(),
            ["challengeId"] = instance.ChallengeId.ToString(),
            ["teamId"] = instance.TeamId.ToString(),
            ["instanceId"] = instance.Id.ToString(),
        };

    private void AddCompetitionLog(
        Guid competitionId,
        string eventType,
        string message,
        string level = "info",
        Guid? teamId = null,
        Guid? challengeId = null,
        object? metadata = null)
    {
        db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim().ToLowerInvariant(),
            EventType = eventType,
            Message = message,
            TeamId = teamId,
            ChallengeId = challengeId,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private sealed record SyncMetadata(
        Guid? TopologyId,
        PenetrationNode? EntryNode,
        int? ExposedPort,
        string EntryConfigJson);
}
