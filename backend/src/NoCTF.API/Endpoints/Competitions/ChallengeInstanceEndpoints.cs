using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Competitions;

public class ChallengeInstanceRequest
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
}

public class ChallengeInstanceResponse
{
    public string? ContainerId { get; set; }
    public Dictionary<int, int> Ports { get; set; } = [];
    public List<string> Addresses { get; set; } = [];
    public string? Address { get; set; }
    public string AccessHost { get; set; } = string.Empty;
    public string Status { get; set; } = "none";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CooldownUntil { get; set; }
    public DateTime ServerTime { get; set; }
}

internal static class ChallengeInstanceRuntime
{
    public static readonly TimeSpan InstanceTtl = TimeSpan.FromHours(2);
    public static readonly TimeSpan ExtendBy = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsActive(AwdGameBox? box, DateTime now)
        => box?.ContainerInstanceId is not null && box.ExpiresAt is not null && box.ExpiresAt > now;

    public static DateTime? CooldownUntil(AwdGameBox? box)
        => box?.LastInstanceActionAt is null ? null : box.LastInstanceActionAt.Value.Add(Cooldown);

    public static bool IsCoolingDown(AwdGameBox? box, DateTime now)
        => CooldownUntil(box) is { } until && until > now;

    public static Dictionary<int, int> ReadPorts(AwdGameBox? box)
    {
        if (string.IsNullOrWhiteSpace(box?.PortMappingsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, int>>(box.PortMappingsJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string WritePorts(Dictionary<int, int> ports)
        => JsonSerializer.Serialize(ports, JsonOptions);

    public static string ResolveAccessHost(IConfiguration configuration, HttpContext httpContext)
    {
        var configured = configuration["InstanceAccess:PublicHost"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        var requestHost = httpContext.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(requestHost) &&
            !IPAddress.IsLoopback(IPAddress.TryParse(requestHost, out var parsed) ? parsed : IPAddress.None) &&
            !requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return requestHost;

        return requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "localhost" : requestHost;
    }

    public static ChallengeInstanceResponse BuildResponse(
        AwdGameBox? box,
        IConfiguration configuration,
        HttpContext httpContext,
        DateTime now)
    {
        var ports = IsActive(box, now) ? ReadPorts(box) : [];
        var host = ResolveAccessHost(configuration, httpContext);
        var addresses = ports.Values
            .Where(port => port > 0)
            .Select(port => $"{FormatHost(host)}:{port}")
            .ToList();

        return new ChallengeInstanceResponse
        {
            ContainerId = IsActive(box, now) ? box?.ContainerInstanceId : null,
            Ports = ports,
            Addresses = addresses,
            Address = addresses.FirstOrDefault(),
            AccessHost = host,
            Status = IsActive(box, now) ? "running" : "none",
            ExpiresAt = IsActive(box, now) ? box?.ExpiresAt : null,
            CooldownUntil = CooldownUntil(box),
            ServerTime = now,
        };
    }

    public static void ClearContainer(AwdGameBox box)
    {
        box.ContainerInstanceId = null;
        box.PortMappingsJson = "{}";
        box.ExpiresAt = null;
    }

    private static string FormatHost(string host)
        => host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
}

public class GetChallengeInstanceEndpoint(ApplicationDbContext dbContext, IConfiguration configuration, IContainerManager containerManager)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/challenges/{challengeId}/instance");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(ChallengeInstanceRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var context = await CreateChallengeInstanceEndpoint.LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (context.Box is not null && context.Box.ContainerInstanceId is not null && !ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
                dbContext,
                containerManager,
                context.Box,
                HttpContext,
                "instance_expired_on_read",
                userId,
                updateCooldown: false,
                ct);
            await dbContext.SaveChangesAsync(ct);
        }

        await SendAsync(ChallengeInstanceRuntime.BuildResponse(context.Box, configuration, HttpContext, DateTime.UtcNow), cancellation: ct);
    }
}

public class CreateChallengeInstanceEndpoint(ApplicationDbContext dbContext, IContainerManager containerManager, IConfiguration configuration)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/instance");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(ChallengeInstanceRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var context = await LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await SendAsync(ChallengeInstanceRuntime.BuildResponse(context.Box, configuration, HttpContext, now), cancellation: ct);
            return;
        }

        if (context.Box is not null && context.Box.ContainerInstanceId is not null)
        {
            await DestroyTrackedBoxAsync(
                dbContext,
                containerManager,
                context.Box,
                HttpContext,
                "instance_expired_before_recreate",
                userId,
                updateCooldown: false,
                ct);
        }

        if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now))
        {
            await SendStringAsync("instance_cooldown", 429, cancellation: ct);
            return;
        }

        var dynamicFlag = await UpsertDynamicFlagAsync(context.Challenge!, context.Team!.Id, ct);
        var instance = await containerManager.CreateContainerAsync(BuildContainerConfig(context.Challenge!, context.Team.Id, dynamicFlag), ct);
        var box = context.Box ?? new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = req.Id,
            TeamId = context.Team.Id,
            ChallengeId = req.ChallengeId,
            CreatedAt = now,
        };

        box.ContainerInstanceId = instance.ContainerId;
        box.PortMappingsJson = ChallengeInstanceRuntime.WritePorts(instance.PortMappings);
        box.ExpiresAt = now.Add(ChallengeInstanceRuntime.InstanceTtl);
        box.LastInstanceActionAt = now;
        if (context.Box is null) dbContext.AwdGameBoxes.Add(box);

        CompetitionLogWriter.Add(
            dbContext,
            req.Id,
            "container.created",
            $"Dynamic container was created for challenge {context.Challenge!.Title}.",
            teamId: context.Team.Id,
            userId: userId,
            challengeId: req.ChallengeId,
            metadata: new { instance.ContainerId, instance.PortMappings, box.ExpiresAt, flagEnv = dynamicFlag.EnvironmentVariable });
        AuditLogWriter.Add(
            dbContext,
            HttpContext,
            "container.instance.created",
            "Container",
            instance.ContainerId,
            new
            {
                competitionId = req.Id,
                challengeId = req.ChallengeId,
                teamId = context.Team.Id,
                containerId = instance.ContainerId,
                ports = instance.PortMappings,
                status = instance.Status,
                expiresAt = box.ExpiresAt,
                flagEnv = dynamicFlag.EnvironmentVariable
            });
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(ChallengeInstanceRuntime.BuildResponse(box, configuration, HttpContext, DateTime.UtcNow), cancellation: ct);
    }

    internal static async Task<ChallengeInstanceContext> LoadContextAsync(
        ApplicationDbContext dbContext,
        ChallengeInstanceRequest req,
        Guid userId,
        CancellationToken ct)
    {
        var team = await dbContext.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.CompetitionId == req.Id && tm.UserId == userId)
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                tm => tm.TeamId,
                t => t.Id,
                (tm, t) => t)
            .FirstOrDefaultAsync(ct);
        if (team is null) return ChallengeInstanceContext.Fail("no_team", 400);
        if (team.RegistrationStatus != TeamRegistrationStatus.Approved) return ChallengeInstanceContext.Fail("team_not_approved", 403);
        if (team.IsBanned) return ChallengeInstanceContext.Fail("team_banned", 403);

        var challenge = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.ChallengeId && c.CompetitionId == req.Id, ct);
        if (challenge is null) return ChallengeInstanceContext.Fail("challenge_not_found", 404);
        if (challenge.DeploymentType != ChallengeDeploymentType.DynamicContainer)
            return ChallengeInstanceContext.Fail("not_dynamic_container", 400);

        var box = await dbContext.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.CompetitionId == req.Id && g.TeamId == team.Id && g.ChallengeId == req.ChallengeId, ct);

        return new ChallengeInstanceContext(team, challenge, box, null);
    }

    private async Task<CtfDynamicFlag> UpsertDynamicFlagAsync(Challenge challenge, Guid teamId, CancellationToken ct)
    {
        var envName = string.IsNullOrWhiteSpace(challenge.FlagEnvironmentVariable)
            ? "NOCTF_FLAG_UUID"
            : challenge.FlagEnvironmentVariable.Trim();
        var existing = await dbContext.CtfDynamicFlags
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.CompetitionId == challenge.CompetitionId &&
                                      f.TeamId == teamId &&
                                      f.ChallengeId == challenge.Id, ct);
        if (existing is not null)
        {
            existing.FlagUuid = Guid.NewGuid().ToString("D");
            existing.EnvironmentVariable = envName;
            existing.CreatedAt = DateTime.UtcNow;
            return existing;
        }

        var flag = new CtfDynamicFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = challenge.CompetitionId,
            TeamId = teamId,
            ChallengeId = challenge.Id,
            FlagUuid = Guid.NewGuid().ToString("D"),
            EnvironmentVariable = envName,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.CtfDynamicFlags.Add(flag);
        return flag;
    }

    internal static ContainerConfig BuildContainerConfig(Challenge challenge, Guid? teamId, CtfDynamicFlag? dynamicFlag = null)
    {
        if (string.IsNullOrWhiteSpace(challenge.ContainerImage))
            throw new InvalidOperationException("challenge_container_image_required");

        var ports = challenge.ExposedPort is > 0
            ? new Dictionary<int, int> { [challenge.ExposedPort.Value] = 0 }
            : null;

        var env = dynamicFlag is null
            ? null
            : new Dictionary<string, string> { [dynamicFlag.EnvironmentVariable] = dynamicFlag.FlagUuid };

        return new ContainerConfig(
            Image: challenge.ContainerImage,
            EnvironmentVariables: env,
            Labels: new Dictionary<string, string>
            {
                ["competitionId"] = challenge.CompetitionId.ToString(),
                ["challengeId"] = challenge.Id.ToString(),
                ["teamId"] = teamId?.ToString() ?? Guid.Empty.ToString(),
            },
            PortMappings: ports,
            Ttl: ChallengeInstanceRuntime.InstanceTtl);
    }

    internal static async Task DestroyTrackedBoxAsync(
        ApplicationDbContext dbContext,
        IContainerManager containerManager,
        AwdGameBox box,
        HttpContext? httpContext,
        string reason,
        Guid? userId,
        bool updateCooldown,
        CancellationToken ct)
    {
        if (box.ContainerInstanceId is null) return;
        var containerId = box.ContainerInstanceId;
        await DestroyBoxAsync(box, containerManager, ct);

        CompetitionLogWriter.Add(
            dbContext,
            box.CompetitionId,
            "container.destroyed",
            "Dynamic container was destroyed.",
            teamId: box.TeamId,
            userId: userId,
            challengeId: box.ChallengeId,
            metadata: new { containerId, reason });

        if (httpContext is not null)
        {
            AuditLogWriter.Add(
                dbContext,
                httpContext,
                "container.instance.destroyed",
                "Container",
                containerId,
                new
                {
                    competitionId = box.CompetitionId,
                    challengeId = box.ChallengeId,
                    teamId = box.TeamId,
                    containerId,
                    reason
                });
        }
        else
        {
            AuditLogWriter.AddSystem(
                dbContext,
                "container.instance.destroyed",
                "Container",
                containerId,
                new
                {
                    competitionId = box.CompetitionId,
                    challengeId = box.ChallengeId,
                    teamId = box.TeamId,
                    containerId,
                    reason
                });
        }

        ChallengeInstanceRuntime.ClearContainer(box);
        if (updateCooldown) box.LastInstanceActionAt = DateTime.UtcNow;
    }

    internal static async Task DestroyBoxAsync(AwdGameBox box, IContainerManager containerManager, CancellationToken ct)
    {
        if (box.ContainerInstanceId is null) return;
        var instance = new ContainerInstance(
            Guid.NewGuid(),
            box.CompetitionId,
            box.TeamId,
            box.ChallengeId,
            "docker",
            box.ContainerInstanceId,
            ChallengeInstanceRuntime.ReadPorts(box),
            "running",
            DateTime.UtcNow);
        await containerManager.DestroyContainerAsync(instance, ct);
    }
}

public class DestroyChallengeInstanceEndpoint(ApplicationDbContext dbContext, IContainerManager containerManager, IConfiguration configuration)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Delete("/api/competitions/{id}/challenges/{challengeId}/instance");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(ChallengeInstanceRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var context = await CreateChallengeInstanceEndpoint.LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (!ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await SendAsync(ChallengeInstanceRuntime.BuildResponse(context.Box, configuration, HttpContext, now), cancellation: ct);
            return;
        }

        if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now))
        {
            await SendStringAsync("instance_cooldown", 429, cancellation: ct);
            return;
        }

        await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
            dbContext,
            containerManager,
            context.Box!,
            HttpContext,
            "player_destroy",
            userId,
            updateCooldown: true,
            ct);
        await dbContext.SaveChangesAsync(ct);
        await SendAsync(ChallengeInstanceRuntime.BuildResponse(context.Box, configuration, HttpContext, DateTime.UtcNow), cancellation: ct);
    }
}

public class ExtendChallengeInstanceEndpoint(ApplicationDbContext dbContext, IConfiguration configuration)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/instance/extend");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(ChallengeInstanceRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var context = await CreateChallengeInstanceEndpoint.LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (!ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await SendStringAsync("instance_not_running", 404, cancellation: ct);
            return;
        }

        if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now))
        {
            await SendStringAsync("instance_cooldown", 429, cancellation: ct);
            return;
        }

        context.Box!.ExpiresAt = (context.Box.ExpiresAt > now ? context.Box.ExpiresAt.Value : now).Add(ChallengeInstanceRuntime.ExtendBy);
        context.Box.LastInstanceActionAt = now;

        CompetitionLogWriter.Add(
            dbContext,
            req.Id,
            "container.extended",
            $"Dynamic container lifetime was extended for challenge {context.Challenge!.Title}.",
            teamId: context.Team!.Id,
            userId: userId,
            challengeId: req.ChallengeId,
            metadata: new { context.Box.ContainerInstanceId, context.Box.ExpiresAt });
        AuditLogWriter.Add(
            dbContext,
            HttpContext,
            "container.instance.extended",
            "Container",
            context.Box.ContainerInstanceId,
            new
            {
                competitionId = req.Id,
                challengeId = req.ChallengeId,
                teamId = context.Team.Id,
                containerId = context.Box.ContainerInstanceId,
                expiresAt = context.Box.ExpiresAt
            });
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(ChallengeInstanceRuntime.BuildResponse(context.Box, configuration, HttpContext, DateTime.UtcNow), cancellation: ct);
    }
}

internal readonly record struct ChallengeInstanceError(string Message, int StatusCode);

internal sealed record ChallengeInstanceContext(
    Team? Team,
    Challenge? Challenge,
    AwdGameBox? Box,
    ChallengeInstanceError? Error)
{
    public static ChallengeInstanceContext Fail(string message, int statusCode)
        => new(null, null, null, new ChallengeInstanceError(message, statusCode));
}
