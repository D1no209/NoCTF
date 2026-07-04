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

    private async Task CleanupExpiredInstancesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var containerManager = scope.ServiceProvider.GetRequiredService<IContainerManager>();
        var maintenanceServices = scope.ServiceProvider.GetServices<IInstanceMaintenanceService>();
        var now = DateTime.UtcNow;

        var expiredBoxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(box => box.ContainerInstanceId != null && box.ExpiresAt != null && box.ExpiresAt <= now)
            .OrderBy(box => box.ExpiresAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var box in expiredBoxes)
        {
            var containerId = box.ContainerInstanceId;
            if (containerId is null) continue;

            try
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
                    OrchestrationNamespace: box.OrchestrationNamespace), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to destroy expired container {ContainerId}. The instance record will still be cleared.", containerId);
            }

            box.ContainerInstanceId = null;
            box.PublicHost = null;
            box.EntryUrl = null;
            box.OrchestrationNamespace = null;
            box.PortMappingsJson = "{}";
            box.ExpiresAt = null;

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
        }

        if (expiredBoxes.Count > 0)
            await db.SaveChangesAsync(ct);

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
            }
            catch (Exception ex)
            {
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
}
