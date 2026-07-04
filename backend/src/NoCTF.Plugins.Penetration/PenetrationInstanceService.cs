using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.Penetration;

public class PenetrationInstanceService(
    ApplicationDbContext db,
    IContainerManager containerManager,
    IConfiguration configuration,
    PenetrationComposeBuilder composeBuilder,
    PenetrationFlagService flagService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PenetrationInstanceStatus[] BusyStatuses =
    [
        PenetrationInstanceStatus.Starting,
        PenetrationInstanceStatus.Stopping,
        PenetrationInstanceStatus.Resetting,
        PenetrationInstanceStatus.Destroying
    ];

    public async Task<PenetrationChallengeDetailDto> GetDetailAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var topology = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId, ct);
        var topologyDto = topology is null
            ? null
            : await BuildTopologyDtoWithSolvesAsync(competitionId, challengeId, teamId, topology.Id, ct);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        var flags = topologyDto?.Flags.Where(f => f.Visible).ToList() ?? [];

        return new PenetrationChallengeDetailDto
        {
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            AuthorizationScope = "Only your team's dedicated target range and entry service are in scope.",
            Topology = topologyDto,
            Instance = ToDto(instance, config),
            TotalStageCount = flags.Count,
            SolvedStageCount = flags.Count(f => f.Solved),
            TotalScore = flags.Sum(f => f.Score),
        };
    }

    public async Task<PenetrationInstanceDto> GetInstanceAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);
        return ToDto(instance, PenetrationTopologyService.ReadConfig(challenge));
    }

    public async Task<PenetrationInstanceDto> StartAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        await EnsureCompetitionAllowsInstanceActionsAsync(competitionId, ct);

        var topology = await LoadTopologyAsync(competitionId, challengeId, ct);
        var nodes = await LoadNodesAsync(competitionId, topology.Id, ct);
        var flags = await LoadFlagsAsync(competitionId, challengeId, ct);
        var now = DateTime.UtcNow;
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);

        if (IsRunning(instance, now))
            return ToDto(instance, config);
        if (instance is not null && BusyStatuses.Contains(instance.Status))
            throw new InvalidOperationException("instance_busy");
        if (IsCoolingDown(instance, config, now))
            throw new InvalidOperationException("instance_cooldown");
        if (instance is not null && instance.ComposeProjectName is not null && instance.Status != PenetrationInstanceStatus.Destroyed)
            await DownBestEffortAsync(instance, ct);

        instance ??= new TeamChallengeInstance
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            TopologyId = topology.Id,
            CreatedAt = now,
        };

        instance.Status = PenetrationInstanceStatus.Starting;
        instance.TopologyId = topology.Id;
        instance.ComposeProjectName = BuildProjectName(competitionId, challengeId, teamId);
        instance.LastActionAt = now;
        instance.UpdatedAt = now;
        instance.LastError = null;
        if (db.Entry(instance).State == EntityState.Detached)
            db.TeamChallengeInstances.Add(instance);
        await db.SaveChangesAsync(ct);

        try
        {
            var dynamicFlags = await flagService.RegenerateDynamicFlagsAsync(challenge, instance, flags, ct);
            var build = composeBuilder.Build(challenge, instance, nodes, flags, dynamicFlags);
            instance.RenderedComposeYaml = build.RedactedComposeYaml;

            await containerManager.ComposeUpAsync(new ComposeConfig(
                ProjectName: instance.ComposeProjectName!,
                ComposeYaml: build.ComposeYaml,
                Labels: BuildLabels(challenge, instance),
                Ttl: TimeSpan.FromSeconds(config.InstanceTtlSeconds)), ct);

            var composeStatus = await containerManager.GetComposeStatusAsync(
                instance.ComposeProjectName!,
                BuildLabels(challenge, instance),
                ct);
            var entryService = composeStatus.Services.FirstOrDefault(s => s.NodeId == build.EntryNode.Id);
            var hostPort = entryService?.PublishedPorts.GetValueOrDefault(build.EntryContainerPort) ?? 0;
            var containerIds = composeStatus.Services.Select(s => s.ContainerId).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();

            instance.Status = PenetrationInstanceStatus.Running;
            instance.EntryHost = ResolveAccessHost();
            instance.EntryPort = hostPort > 0 ? hostPort : null;
            instance.EntryUrl = BuildEntryUrl(instance.EntryHost, instance.EntryPort, topology.EntryConfigJson);
            instance.ContainerIdsJson = JsonSerializer.Serialize(containerIds, JsonOptions);
            instance.PortMappingsJson = hostPort > 0
                ? JsonSerializer.Serialize(new Dictionary<int, int> { [build.EntryContainerPort] = hostPort }, JsonOptions)
                : "{}";
            instance.ExpiresAt = now.AddSeconds(config.InstanceTtlSeconds);
            instance.UpdatedAt = DateTime.UtcNow;

            AddCompetitionLog(
                db,
                competitionId,
                "penetration.instance.started",
                "Penetration range instance was started.",
                teamId: teamId,
                userId: userId,
                challengeId: challengeId,
                metadata: new
                {
                    instanceId = instance.Id,
                    projectName = instance.ComposeProjectName,
                    entryPort = instance.EntryPort,
                    expiresAt = instance.ExpiresAt
                });
            await db.SaveChangesAsync(ct);
            return ToDto(instance, config);
        }
        catch (Exception ex)
        {
            await DownBestEffortAsync(instance, ct);
            await DeactivateDynamicFlagsAsync(instance, ct);
            instance.Status = PenetrationInstanceStatus.Failed;
            instance.LastError = ex.Message;
            instance.UpdatedAt = DateTime.UtcNow;
            AddCompetitionLog(
                db,
                competitionId,
                "penetration.instance.start_failed",
                "Penetration range instance failed to start.",
                "error",
                teamId,
                userId,
                challengeId,
                metadata: new { instanceId = instance.Id, error = ex.Message });
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<PenetrationInstanceDto> StopAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        if (instance is null) return ToDto(null, config);
        if (BusyStatuses.Contains(instance.Status)) throw new InvalidOperationException("instance_busy");

        instance.Status = PenetrationInstanceStatus.Stopping;
        instance.LastActionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await DownBestEffortAsync(instance, ct);
        await DeactivateDynamicFlagsAsync(instance, ct);
        instance.Status = PenetrationInstanceStatus.Stopped;
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.ContainerIdsJson = "[]";
        instance.PortMappingsJson = "{}";
        instance.UpdatedAt = DateTime.UtcNow;
        AddCompetitionLog(db, competitionId, "penetration.instance.stopped", "Penetration range instance was stopped.", teamId: teamId, userId: userId, challengeId: challengeId, metadata: new { instanceId = instance.Id });
        await db.SaveChangesAsync(ct);
        return ToDto(instance, config);
    }

    public async Task<PenetrationInstanceDto> ResetAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        bool adminOverride,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        if (!adminOverride)
            await EnsureCompetitionAllowsInstanceActionsAsync(competitionId, ct);
        if (!config.AllowReset && !adminOverride)
            throw new InvalidOperationException("reset_not_allowed");

        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct)
            ?? throw new InvalidOperationException("instance_not_found");
        if (BusyStatuses.Contains(instance.Status)) throw new InvalidOperationException("instance_busy");
        if (!adminOverride && instance.ResetCount >= config.MaxResetCount)
            throw new InvalidOperationException("reset_limit_exceeded");
        if (!adminOverride && IsCoolingDown(instance, config, DateTime.UtcNow))
            throw new InvalidOperationException("instance_cooldown");

        instance.Status = PenetrationInstanceStatus.Resetting;
        instance.LastActionAt = DateTime.UtcNow;
        instance.ResetCount += 1;
        await db.SaveChangesAsync(ct);
        await DownBestEffortAsync(instance, ct);
        instance.Status = PenetrationInstanceStatus.Stopped;
        instance.LastActionAt = null;
        await db.SaveChangesAsync(ct);
        AddCompetitionLog(db, competitionId, adminOverride ? "penetration.admin.instance_reset" : "penetration.instance.reset", "Penetration range instance was reset.", teamId: teamId, userId: userId, challengeId: challengeId, metadata: new { instanceId = instance.Id, instance.ResetCount });
        await db.SaveChangesAsync(ct);
        return await StartAsync(competitionId, challengeId, teamId, userId, ct);
    }

    public async Task<PenetrationInstanceDto> DestroyAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        bool adminOverride,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);
        if (instance is null) return ToDto(null, config);
        if (BusyStatuses.Contains(instance.Status)) throw new InvalidOperationException("instance_busy");

        instance.Status = PenetrationInstanceStatus.Destroying;
        instance.LastActionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await DownBestEffortAsync(instance, ct);
        await DeactivateDynamicFlagsAsync(instance, ct);

        instance.Status = PenetrationInstanceStatus.Destroyed;
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.ContainerIdsJson = "[]";
        instance.PortMappingsJson = "{}";
        instance.UpdatedAt = DateTime.UtcNow;
        AddCompetitionLog(db, competitionId, adminOverride ? "penetration.admin.instance_destroy" : "penetration.instance.destroyed", "Penetration range instance was destroyed.", teamId: teamId, userId: userId, challengeId: challengeId, metadata: new { instanceId = instance.Id });
        await db.SaveChangesAsync(ct);
        return ToDto(instance, config);
    }

    public async Task<object> ListAdminInstancesAsync(Guid competitionId, Guid? challengeId, Guid? teamId, CancellationToken ct)
    {
        var query = db.TeamChallengeInstances.IgnoreQueryFilters().AsNoTracking().Where(i => i.CompetitionId == competitionId);
        if (challengeId.HasValue) query = query.Where(i => i.ChallengeId == challengeId.Value);
        if (teamId.HasValue) query = query.Where(i => i.TeamId == teamId.Value);

        var rows = await query
            .Join(db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t => t.CompetitionId == competitionId),
                i => i.TeamId,
                t => t.Id,
                (i, t) => new { Instance = i, TeamName = t.Name })
            .Join(db.Challenges.IgnoreQueryFilters().AsNoTracking().Where(c => c.CompetitionId == competitionId),
                row => row.Instance.ChallengeId,
                c => c.Id,
                (row, c) => new
                {
                    id = row.Instance.Id,
                    row.Instance.TeamId,
                    row.TeamName,
                    row.Instance.ChallengeId,
                    challengeTitle = c.Title,
                    status = row.Instance.Status.ToString(),
                    row.Instance.EntryUrl,
                    row.Instance.ResetCount,
                    row.Instance.ExpiresAt,
                    row.Instance.LastError,
                    row.Instance.CreatedAt,
                    row.Instance.UpdatedAt
                })
            .OrderBy(row => row.challengeTitle)
            .ThenBy(row => row.TeamName)
            .ToListAsync(ct);

        return new { items = rows };
    }

    public async Task<(TeamChallengeInstance Instance, Challenge Challenge)> LoadInstanceForAdminAsync(
        Guid competitionId,
        Guid instanceId,
        CancellationToken ct)
    {
        var instance = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.CompetitionId == competitionId && i.Id == instanceId, ct)
            ?? throw new InvalidOperationException("instance_not_found");
        var challenge = await LoadChallengeAsync(competitionId, instance.ChallengeId, ct);
        return (instance, challenge);
    }

    private async Task<PenetrationTopologyDto> BuildTopologyDtoWithSolvesAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid topologyId,
        CancellationToken ct)
    {
        var topologyService = new PenetrationTopologyService(db);
        var dto = await topologyService.GetCompetitionTopologyAsync(competitionId, challengeId, ct);
        var solved = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.TeamId == teamId &&
                s.ChallengeId == challengeId &&
                s.IsCorrect &&
                s.PenetrationFlagId != null)
            .Select(s => new { FlagId = s.PenetrationFlagId!.Value, s.SubmittedAt })
            .ToListAsync(ct);

        foreach (var flag in dto.Flags)
        {
            var solvedFlag = solved.FirstOrDefault(s => s.FlagId == flag.Id);
            flag.Solved = solvedFlag is not null;
            flag.SolvedAt = solvedFlag?.SubmittedAt;
        }

        return dto;
    }

    private async Task<Challenge> LoadChallengeAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompetitionId == competitionId && c.Id == challengeId, ct)
            ?? throw new InvalidOperationException("challenge_not_found");
        if (!string.Equals(challenge.TypeId, PenetrationConstants.TypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("not_penetration_challenge");
        return challenge;
    }

    private async Task<PenetrationTopology> LoadTopologyAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
        => await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId, ct)
            ?? throw new InvalidOperationException("penetration_topology_not_configured");

    private Task<List<PenetrationNode>> LoadNodesAsync(Guid competitionId, Guid topologyId, CancellationToken ct)
        => db.PenetrationNodes
            .IgnoreQueryFilters()
            .Where(n => n.CompetitionId == competitionId && n.TopologyId == topologyId)
            .OrderBy(n => n.DisplayOrder)
            .ToListAsync(ct);

    private Task<List<PenetrationFlag>> LoadFlagsAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
        => db.PenetrationFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId)
            .OrderBy(f => f.Stage)
            .ToListAsync(ct);

    private Task<TeamChallengeInstance?> LoadInstanceAsync(Guid competitionId, Guid challengeId, Guid teamId, CancellationToken ct)
        => db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.CompetitionId == competitionId && i.TeamId == teamId && i.ChallengeId == challengeId, ct);

    private async Task EnsureCompetitionAllowsInstanceActionsAsync(Guid competitionId, CancellationToken ct)
    {
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == competitionId, ct)
            ?? throw new InvalidOperationException("competition_not_found");
        var now = DateTime.UtcNow;
        if (now < competition.StartTime) throw new InvalidOperationException("competition_not_started");
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished) throw new InvalidOperationException("competition_ended");
        if (competition.Status == CompetitionStatus.Paused) throw new InvalidOperationException("competition_paused");
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
        catch
        {
            // Cleanup is best-effort here; the caller records the operation outcome.
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

    private static bool IsRunning(TeamChallengeInstance? instance, DateTime now)
        => instance?.Status == PenetrationInstanceStatus.Running &&
           (!instance.ExpiresAt.HasValue || instance.ExpiresAt.Value > now);

    private static bool IsCoolingDown(TeamChallengeInstance? instance, PenetrationRuntimeConfig config, DateTime now)
        => instance?.LastActionAt is { } lastAction &&
           lastAction.AddSeconds(Math.Max(0, config.ActionCooldownSeconds)) > now;

    private static PenetrationInstanceDto ToDto(TeamChallengeInstance? instance, PenetrationRuntimeConfig config)
    {
        if (instance is null)
        {
            return new PenetrationInstanceDto
            {
                ResetLimit = config.MaxResetCount,
                ServerTime = DateTime.UtcNow,
            };
        }

        return new PenetrationInstanceDto
        {
            Id = instance.Id,
            Status = instance.Status.ToString(),
            EntryHost = instance.EntryHost,
            EntryPort = instance.EntryPort,
            EntryUrl = config.VisibleEntryAfterStart ? instance.EntryUrl : null,
            ResetCount = instance.ResetCount,
            ResetLimit = config.MaxResetCount,
            ExpiresAt = instance.ExpiresAt,
            CooldownUntil = instance.LastActionAt?.AddSeconds(Math.Max(0, config.ActionCooldownSeconds)),
            ServerTime = DateTime.UtcNow,
            LastError = instance.LastError,
            ContainerIds = ReadStringArray(instance.ContainerIdsJson),
            Ports = ReadPortMap(instance.PortMappingsJson),
        };
    }

    private string ResolveAccessHost()
    {
        var configured = configuration["InstanceAccess:PublicHost"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return "127.0.0.1";
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

    private static Dictionary<string, string> BuildLabels(Challenge challenge, TeamChallengeInstance instance)
        => new()
        {
            ["noctf.kind"] = "penetration",
            ["competitionId"] = challenge.CompetitionId.ToString(),
            ["challengeId"] = challenge.Id.ToString(),
            ["teamId"] = instance.TeamId.ToString(),
            ["instanceId"] = instance.Id.ToString(),
        };

    private static void AddCompetitionLog(
        ApplicationDbContext db,
        Guid competitionId,
        string eventType,
        string message,
        string level = "info",
        Guid? teamId = null,
        Guid? userId = null,
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
            UserId = userId,
            ChallengeId = challengeId,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string BuildProjectName(Guid competitionId, Guid challengeId, Guid teamId)
        => $"noctf-pen-{ShortId(competitionId)}-{ShortId(challengeId)}-{ShortId(teamId)}-{ShortId(Guid.NewGuid())}";

    private static string ShortId(Guid id)
        => id.ToString("N")[..8];

    private static string[] ReadStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? []; }
        catch { return []; }
    }

    private static Dictionary<int, int> ReadPortMap(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<int, int>>(json, JsonOptions) ?? []; }
        catch { return []; }
    }
}
