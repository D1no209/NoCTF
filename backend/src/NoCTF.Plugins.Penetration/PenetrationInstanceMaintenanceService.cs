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
    ILogger<PenetrationInstanceMaintenanceService> logger) : IInstanceMaintenanceService
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
            if (await ExpireAsync(instance, ct))
                expiredCount++;
            else
                failedCount++;
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

        var syncedCount = 0;
        foreach (var instance in syncCandidates)
        {
            if (await SyncStatusAsync(instance, ct))
                syncedCount++;
            else
                failedCount++;
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
            await MarkStuckInstanceFailedAsync(instance, ct);
            failedCount++;
        }

        if (expiredCount > 0 || syncedCount > 0 || failedCount > 0)
            await db.SaveChangesAsync(ct);

        return new InstanceMaintenanceResult(expiredCount, syncedCount, failedCount);
    }

    private async Task<bool> ExpireAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        try
        {
            await DownBestEffortAsync(instance, ct);
            await DeactivateDynamicFlagsAsync(instance, ct);

            var now = DateTime.UtcNow;
            instance.Status = PenetrationInstanceStatus.Expired;
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
            instance.LastError = ex.Message;
            instance.UpdatedAt = DateTime.UtcNow;
            return false;
        }
    }

    private async Task<bool> SyncStatusAsync(TeamChallengeInstance instance, CancellationToken ct)
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
                instance.EntryPort = null;
                instance.EntryUrl = null;
                instance.PortMappingsJson = "{}";
                instance.LastError = null;
                instance.UpdatedAt = DateTime.UtcNow;
                return true;
            }

            var challenge = await db.Challenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompetitionId == instance.CompetitionId && c.Id == instance.ChallengeId, ct);
            if (challenge is null)
                return true;

            var entryNode = await db.PenetrationNodes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(n => n.CompetitionId == instance.CompetitionId && n.TopologyId == instance.TopologyId && n.IsEntry)
                .OrderBy(n => n.DisplayOrder)
                .FirstOrDefaultAsync(ct);
            if (entryNode is not null)
            {
                var entryContainerPort = PenetrationComposeBuilder.GetPrimaryContainerPort(entryNode, challenge.ExposedPort);
                var entryService = status.Services.FirstOrDefault(s => s.NodeId == entryNode.Id);
                var hostPort = entryService?.PublishedPorts.GetValueOrDefault(entryContainerPort) ?? 0;
                if (hostPort > 0)
                {
                    instance.EntryHost = !string.IsNullOrWhiteSpace(entryService?.PublicHost)
                        ? entryService.PublicHost
                        : ResolveAccessHost();
                    instance.EntryPort = hostPort;
                    instance.EntryUrl = !string.IsNullOrWhiteSpace(entryService?.EntryUrl)
                        ? entryService.EntryUrl
                        : BuildEntryUrl(instance.EntryHost, hostPort, await ReadEntryConfigJsonAsync(instance, ct));
                    instance.PortMappingsJson = JsonSerializer.Serialize(new Dictionary<int, int> { [entryContainerPort] = hostPort }, JsonOptions);
                }
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
        string? cleanupError = null;
        try
        {
            await DownBestEffortAsync(instance, ct);
        }
        catch (Exception ex)
        {
            cleanupError = ex.Message;
        }

        instance.Status = PenetrationInstanceStatus.Failed;
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.ContainerIdsJson = "[]";
        instance.PortMappingsJson = "{}";
        instance.LastError = cleanupError is null
            ? $"Instance maintenance timed out while {previousStatus}."
            : $"Instance maintenance timed out while {previousStatus}; cleanup failed: {cleanupError}";
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
        if (string.IsNullOrWhiteSpace(instance.ComposeProjectName) || string.IsNullOrWhiteSpace(instance.RenderedComposeYaml))
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
                ComposeYaml: instance.RenderedComposeYaml,
                Status: instance.Status.ToString(),
                StartedAt: instance.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Compose down failed for penetration instance {InstanceId}. Runtime state is preserved for retry.", instance.Id);
            instance.LastError = ex.Message;
            instance.UpdatedAt = DateTime.UtcNow;
            throw;
        }
    }

    private async Task DeactivateDynamicFlagsAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        var flags = await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == instance.CompetitionId &&
                f.TeamId == instance.TeamId &&
                f.ChallengeId == instance.ChallengeId &&
                f.InstanceId == instance.Id &&
                f.IsActive)
            .ToListAsync(ct);

        foreach (var flag in flags)
            flag.IsActive = false;
    }

    private async Task<string> ReadEntryConfigJsonAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        if (instance.TopologyId is null)
            return "{}";

        var topology = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompetitionId == instance.CompetitionId && t.Id == instance.TopologyId.Value, ct);

        return topology?.EntryConfigJson ?? "{}";
    }

    private string ResolveAccessHost()
    {
        var configured = configuration["InstanceAccess:PublicHost"];
        return string.IsNullOrWhiteSpace(configured) ? "127.0.0.1" : configured.Trim();
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
}
