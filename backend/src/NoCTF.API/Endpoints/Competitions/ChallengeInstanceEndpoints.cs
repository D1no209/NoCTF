using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.Application.BackgroundTasks;
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
    public string? EntryUrl { get; set; }
    public string AccessHost { get; set; } = string.Empty;
    public string Status { get; set; } = "none";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CooldownUntil { get; set; }
    public DateTime ServerTime { get; set; }
}

public static class ChallengeInstanceRuntime
{
    public static readonly TimeSpan InstanceTtl = TimeSpan.FromHours(2);
    public static readonly TimeSpan ExtendBy = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AwdpCooldown = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsActive(AwdGameBox? box, DateTime now)
        => box?.ContainerInstanceId is not null && box.ExpiresAt is not null && box.ExpiresAt > now;

    public static DateTime? CooldownUntil(AwdGameBox? box)
        => box?.LastInstanceActionAt is null ? null : box.LastInstanceActionAt.Value.Add(Cooldown);

    public static DateTime? CooldownUntil(AwdGameBox? box, GameModeType gameModeType)
        => box?.LastInstanceActionAt is null
            ? null
            : box.LastInstanceActionAt.Value.Add(GetCooldown(gameModeType));

    public static bool IsCoolingDown(AwdGameBox? box, DateTime now)
        => CooldownUntil(box) is { } until && until > now;

    public static bool IsCoolingDown(AwdGameBox? box, DateTime now, GameModeType gameModeType)
        => CooldownUntil(box, gameModeType) is { } until && until > now;

    public static bool SupportsPlayerManagedInstance(GameModeType gameModeType)
        => gameModeType is GameModeType.Ctf or GameModeType.Awdp;

    public static string BuildTransitionKey(Guid teamId, Guid challengeId)
        => CompetitionExecutionLeaseKeys.ChallengeInstance(teamId, challengeId);

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

        if (!configuration.GetValue("InstanceAccess:TrustRequestHost", false))
            return string.Empty;

        var requestHost = httpContext.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(requestHost) &&
            !IPAddress.IsLoopback(IPAddress.TryParse(requestHost, out var parsed) ? parsed : IPAddress.None) &&
            !requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return requestHost;

        var environment = configuration["ASPNETCORE_ENVIRONMENT"];
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "localhost" : requestHost;
    }

    public static ChallengeInstanceResponse BuildResponse(
        AwdGameBox? box,
        IConfiguration configuration,
        HttpContext httpContext,
        DateTime now)
        => BuildResponse(box, configuration, httpContext, now, GameModeType.Ctf);

    public static ChallengeInstanceResponse BuildResponse(
        AwdGameBox? box,
        IConfiguration configuration,
        HttpContext httpContext,
        DateTime now,
        GameModeType gameModeType)
    {
        var ports = IsActive(box, now) ? ReadPorts(box) : [];
        var host = !string.IsNullOrWhiteSpace(box?.PublicHost)
            ? box.PublicHost
            : ResolveAccessHost(configuration, httpContext);
        var entryUrl = IsActive(box, now) && !string.IsNullOrWhiteSpace(box?.EntryUrl)
            ? box.EntryUrl
            : null;
        var addresses = entryUrl is not null
            ? [entryUrl]
            : string.IsNullOrWhiteSpace(host)
                ? []
                : ports.Values
                .Where(port => port > 0)
                .Select(port => $"{FormatHost(host)}:{port}")
                .ToList();

        return new ChallengeInstanceResponse
        {
            ContainerId = IsActive(box, now) ? box?.ContainerInstanceId : null,
            Ports = ports,
            Addresses = addresses,
            Address = addresses.FirstOrDefault(),
            EntryUrl = entryUrl,
            AccessHost = host,
            Status = IsActive(box, now) ? "running" : "none",
            ExpiresAt = IsActive(box, now) ? box?.ExpiresAt : null,
            CooldownUntil = CooldownUntil(box, gameModeType),
            ServerTime = now,
        };
    }

    public static void ClearContainer(AwdGameBox box)
    {
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
    }

    private static string FormatHost(string host)
        => host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;

    private static TimeSpan GetCooldown(GameModeType gameModeType)
        => gameModeType switch
        {
            GameModeType.Ctf => Cooldown,
            GameModeType.Awdp => AwdpCooldown,
            _ => throw new ArgumentOutOfRangeException(nameof(gameModeType), gameModeType, "Unsupported player-managed instance mode.")
        };
}

public class GetChallengeInstanceEndpoint(
    ApplicationDbContext dbContext,
    IConfiguration configuration,
    IContainerManager containerManager,
    ICompetitionExecutionLease executionLease)
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
        var blockReason = PublicCompetitionGuard.ResolvePlayBlockReason(context.Competition!, now);
        var requiresCleanup = context.Box?.ContainerInstanceId is not null &&
                              (blockReason is not null || !ChallengeInstanceRuntime.IsActive(context.Box, now));
        IExecutionLease? transitionLease = null;
        CancellationTokenSource? leaseCts = null;
        if (requiresCleanup)
        {
            transitionLease = await executionLease.TryAcquireAsync(
                dbContext,
                ChallengeInstanceRuntime.BuildTransitionKey(context.Team!.Id, req.ChallengeId),
                req.Id,
                ct);
            if (transitionLease is null)
            {
                await SendStringAsync("instance_transition_in_progress", 409, cancellation: ct);
                return;
            }

            leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
            ct = leaseCts.Token;
            dbContext.ChangeTracker.Clear();
            context = await CreateChallengeInstanceEndpoint.LoadContextAsync(dbContext, req, userId, ct);
            if (context.Error is not null)
            {
                await transitionLease.DisposeAsync();
                leaseCts.Dispose();
                await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
                return;
            }

            now = DateTime.UtcNow;
            blockReason = PublicCompetitionGuard.ResolvePlayBlockReason(context.Competition!, now);
        }

        try
        {
            if (blockReason is not null)
            {
                if (context.Box is not null && context.Box.ContainerInstanceId is not null)
                {
                    await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
                        dbContext,
                        containerManager,
                        context.Box,
                        HttpContext,
                        blockReason,
                        userId,
                        updateCooldown: false,
                        ct);
                    await dbContext.SaveChangesAsync(ct);
                }

                await SendStringAsync(blockReason, 403, cancellation: ct);
                return;
            }

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

            await SendAsync(ChallengeInstanceRuntime.BuildResponse(
                context.Box,
                configuration,
                HttpContext,
                DateTime.UtcNow,
                context.Competition!.GameModeType), cancellation: ct);
        }
        finally
        {
            if (transitionLease is not null)
                await transitionLease.DisposeAsync();
            leaseCts?.Dispose();
        }
    }
}

public class CreateChallengeInstanceEndpoint(
    ApplicationDbContext dbContext,
    IContainerManager containerManager,
    IConfiguration configuration,
    ICompetitionExecutionLease executionLease)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/instance");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
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

        await using var transitionLease = await executionLease.TryAcquireAsync(
            dbContext,
            ChallengeInstanceRuntime.BuildTransitionKey(context.Team!.Id, req.ChallengeId),
            req.Id,
            ct);
        if (transitionLease is null)
        {
            await SendStringAsync("instance_transition_in_progress", 409, cancellation: ct);
            return;
        }
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        ct = leaseCts.Token;

        dbContext.ChangeTracker.Clear();
        context = await LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (PublicCompetitionGuard.ResolvePlayBlockReason(context.Competition!, now) is { } blockReason)
        {
            if (context.Box is not null && context.Box.ContainerInstanceId is not null)
            {
                await DestroyTrackedBoxAsync(
                    dbContext,
                    containerManager,
                    context.Box,
                    HttpContext,
                    blockReason,
                    userId,
                    updateCooldown: false,
                    ct);
                await dbContext.SaveChangesAsync(ct);
            }

            await SendStringAsync(blockReason, 403, cancellation: ct);
            return;
        }

        if (ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await SendAsync(ChallengeInstanceRuntime.BuildResponse(
                context.Box,
                configuration,
                HttpContext,
                now,
                context.Competition!.GameModeType), cancellation: ct);
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
            // Commit removal of the previous dynamic flag before creating the
            // replacement. Otherwise EF can return the tracked Deleted entity
            // from the upsert query and silently delete the newly issued flag.
            await dbContext.SaveChangesAsync(ct);
        }

        if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now, context.Competition!.GameModeType))
        {
            await SendStringAsync("instance_cooldown", 429, cancellation: ct);
            return;
        }

        AwdGameBox box;
        CtfDynamicFlag dynamicFlag;
        await using (var preparationLease = await executionLease.TryAcquireAsync(
            dbContext,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            req.Id,
            ct))
        {
            if (preparationLease is null)
            {
                await SendStringAsync("runtime_preparation_in_progress", 409, cancellation: ct);
                return;
            }

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                preparationLease.LostToken);
            var preparationCt = preparationCts.Token;

            // Re-read every admission tombstone inside the short preparation
            // lease. A deletion that wins this lease rejects the create; a
            // create that wins persists its candidate before deletion scans.
            dbContext.ChangeTracker.Clear();
            context = await LoadContextAsync(dbContext, req, userId, preparationCt);
            if (context.Error is not null)
            {
                await SendStringAsync(
                    context.Error.Value.Message,
                    context.Error.Value.StatusCode,
                    cancellation: preparationCt);
                return;
            }

            now = DateTime.UtcNow;
            if (PublicCompetitionGuard.ResolvePlayBlockReason(context.Competition!, now) is { } preparationBlock)
            {
                await SendStringAsync(preparationBlock, 403, cancellation: preparationCt);
                return;
            }
            if (ChallengeInstanceRuntime.IsActive(context.Box, now))
            {
                await SendAsync(
                    ChallengeInstanceRuntime.BuildResponse(
                        context.Box,
                        configuration,
                        HttpContext,
                        now,
                        context.Competition!.GameModeType),
                    cancellation: preparationCt);
                return;
            }
            if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now, context.Competition!.GameModeType))
            {
                await SendStringAsync("instance_cooldown", 429, cancellation: preparationCt);
                return;
            }

            box = context.Box ?? new AwdGameBox
            {
                Id = Guid.NewGuid(),
                CompetitionId = req.Id,
                TeamId = context.Team!.Id,
                ChallengeId = req.ChallengeId,
                CreatedAt = now,
            };
            var isPendingRetry = box.ContainerInstanceId is null && box.RuntimeOperationId.HasValue;
            box.RuntimeOperationId ??= Guid.NewGuid();
            if (context.Box is null)
                dbContext.AwdGameBoxes.Add(box);

            dynamicFlag = await UpsertDynamicFlagAsync(
                context.Challenge!,
                context.Team!.Id,
                regenerateExisting: !isPendingRetry,
                preparationCt);

            // Persist the operation id and flag before crossing the Runner boundary.
            // A retry after an ambiguous response can then recover the same runtime.
            await dbContext.SaveChangesAsync(preparationCt);
        }

        var instance = await containerManager.CreateContainerAsync(
            BuildContainerConfig(
                context.Challenge!,
                context.Team.Id,
                dynamicFlag,
                box.RuntimeOperationId),
            ct);

        box.ContainerInstanceId = instance.ContainerId;
        box.ProviderType = instance.ProviderType;
        box.PublicHost = instance.PublicHost;
        box.EntryUrl = instance.EntryUrl;
        box.OrchestrationNamespace = instance.OrchestrationNamespace;
        box.PortMappingsJson = ChallengeInstanceRuntime.WritePorts(instance.PortMappings);
        var persistedAt = DateTime.UtcNow;
        box.ExpiresAt = instance.ExpectedStopAt ?? persistedAt.Add(ChallengeInstanceRuntime.InstanceTtl);
        box.LastInstanceActionAt = persistedAt;

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
        // Keep RuntimeOperationId until destroy. If this metadata save fails,
        // the next request can reuse the Runner receipt instead of duplicating
        // or prematurely deleting the runtime.
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(ChallengeInstanceRuntime.BuildResponse(
            box,
            configuration,
            HttpContext,
            DateTime.UtcNow,
            context.Competition!.GameModeType), cancellation: ct);
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

        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);
        if (competition is null) return ChallengeInstanceContext.Fail("competition_not_found", 404);
        if (!ChallengeInstanceRuntime.SupportsPlayerManagedInstance(competition.GameModeType))
            return ChallengeInstanceContext.Fail("mode_unavailable", 503);

        var challenge = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == req.ChallengeId &&
                c.CompetitionId == req.Id &&
                !c.IsDeleting,
                ct);
        if (challenge is null) return ChallengeInstanceContext.Fail("challenge_not_found", 404);
        if (string.Equals(challenge.TypeId, "Penetration", StringComparison.OrdinalIgnoreCase))
            return ChallengeInstanceContext.Fail("use_penetration_instance_api", 400);
        if (challenge.DeploymentType != ChallengeDeploymentType.DynamicContainer)
            return ChallengeInstanceContext.Fail("not_dynamic_container", 400);

        var box = await dbContext.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.CompetitionId == req.Id && g.TeamId == team.Id && g.ChallengeId == req.ChallengeId, ct);

        return new ChallengeInstanceContext(team, competition, challenge, box, null);
    }

    private async Task<CtfDynamicFlag> UpsertDynamicFlagAsync(
        Challenge challenge,
        Guid teamId,
        bool regenerateExisting,
        CancellationToken ct)
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
            if (regenerateExisting)
                existing.FlagUuid = Guid.NewGuid().ToString("D");
            existing.EnvironmentVariable = envName;
            if (regenerateExisting)
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

    internal static ContainerConfig BuildContainerConfig(
        Challenge challenge,
        Guid? teamId,
        CtfDynamicFlag? dynamicFlag = null,
        Guid? operationId = null)
    {
        var spec = OrchestrationSpecSerializer.Read(challenge.OrchestrationJson);
        var image = string.IsNullOrWhiteSpace(spec.Image) ? challenge.ContainerImage : spec.Image;
        if (string.IsNullOrWhiteSpace(image))
            throw new InvalidOperationException("challenge_container_image_required");

        var exposedPort = spec.ExposedPort is > 0 ? spec.ExposedPort : challenge.ExposedPort;
        var ports = exposedPort is > 0
            ? new Dictionary<int, int> { [exposedPort.Value] = 0 }
            : null;

        var env = spec.Environment.Count > 0
            ? new Dictionary<string, string>(spec.Environment, StringComparer.Ordinal)
            : null;
        if (dynamicFlag is not null)
        {
            env ??= new Dictionary<string, string>(StringComparer.Ordinal);
            env[dynamicFlag.EnvironmentVariable] = FormatFlag(challenge, dynamicFlag.FlagUuid);
        }

        return new ContainerConfig(
            Image: image,
            Command: string.IsNullOrWhiteSpace(spec.Command) ? null : spec.Command,
            EnvironmentVariables: env,
            Labels: new Dictionary<string, string>
            {
                ["competitionId"] = challenge.CompetitionId.ToString(),
                ["challengeId"] = challenge.Id.ToString(),
                ["teamId"] = teamId?.ToString() ?? Guid.Empty.ToString(),
            },
            PortMappings: ports,
            Entrypoint: spec.Entrypoint.Count > 0 ? spec.Entrypoint : null,
            OrchestrationJson: challenge.OrchestrationJson,
            Ttl: ChallengeInstanceRuntime.InstanceTtl,
            NetworkAliases: teamId.HasValue ? [BuildGameBoxAlias(teamId.Value, challenge.Id)] : null,
            OperationId: operationId ?? dynamicFlag?.Id);
    }

    internal static string BuildGameBoxAlias(Guid teamId, Guid challengeId)
        => $"gamebox-{ShortId(teamId)}-{ShortId(challengeId)}";

    private static string ShortId(Guid id)
        => id.ToString("N")[..8];

    private static string FormatFlag(Challenge challenge, string content)
    {
        var prefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix.Trim();
        if (prefix.Contains("{0}", StringComparison.Ordinal))
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, prefix, content);

        if (prefix.Contains("{}", StringComparison.Ordinal))
            return prefix.Replace("{}", $"{{{content}}}", StringComparison.Ordinal);

        var braceIndex = prefix.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex >= 0)
            prefix = prefix[..braceIndex].Trim();

        return $"{prefix}{{{content}}}";
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
        var dynamicFlags = await dbContext.CtfDynamicFlags
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == box.CompetitionId &&
                f.TeamId == box.TeamId &&
                f.ChallengeId == box.ChallengeId)
            .ToListAsync(ct);
        dbContext.CtfDynamicFlags.RemoveRange(dynamicFlags);
        if (updateCooldown) box.LastInstanceActionAt = DateTime.UtcNow;
    }

    internal static async Task DestroyBoxAsync(AwdGameBox box, IContainerManager containerManager, CancellationToken ct)
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
                box.OrchestrationNamespace), ct);
            return;
        }

        if (box.ContainerInstanceId is null) return;
        var instance = new ContainerInstance(
            Guid.NewGuid(),
            box.CompetitionId,
            box.TeamId,
            box.ChallengeId,
            box.ProviderType,
            box.ContainerInstanceId,
            ChallengeInstanceRuntime.ReadPorts(box),
            "running",
            DateTime.UtcNow,
            OrchestrationNamespace: box.OrchestrationNamespace);
        await containerManager.DestroyContainerAsync(instance, ct);
    }
}

public class DestroyChallengeInstanceEndpoint(
    ApplicationDbContext dbContext,
    IContainerManager containerManager,
    IConfiguration configuration,
    ICompetitionExecutionLease executionLease)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Delete("/api/competitions/{id}/challenges/{challengeId}/instance");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
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

        await using var transitionLease = await executionLease.TryAcquireAsync(
            dbContext,
            ChallengeInstanceRuntime.BuildTransitionKey(context.Team!.Id, req.ChallengeId),
            req.Id,
            ct);
        if (transitionLease is null)
        {
            await SendStringAsync("instance_transition_in_progress", 409, cancellation: ct);
            return;
        }
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        ct = leaseCts.Token;

        dbContext.ChangeTracker.Clear();
        context = await CreateChallengeInstanceEndpoint.LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (!ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await SendAsync(ChallengeInstanceRuntime.BuildResponse(
                context.Box,
                configuration,
                HttpContext,
                now,
                context.Competition!.GameModeType), cancellation: ct);
            return;
        }

        if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now, context.Competition!.GameModeType))
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
        await SendAsync(ChallengeInstanceRuntime.BuildResponse(
            context.Box,
            configuration,
            HttpContext,
            DateTime.UtcNow,
            context.Competition!.GameModeType), cancellation: ct);
    }
}

public class ExtendChallengeInstanceEndpoint(
    ApplicationDbContext dbContext,
    IConfiguration configuration,
    ICompetitionExecutionLease executionLease)
    : Endpoint<ChallengeInstanceRequest, ChallengeInstanceResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/instance/extend");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
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

        await using var transitionLease = await executionLease.TryAcquireAsync(
            dbContext,
            ChallengeInstanceRuntime.BuildTransitionKey(context.Team!.Id, req.ChallengeId),
            req.Id,
            ct);
        if (transitionLease is null)
        {
            await SendStringAsync("instance_transition_in_progress", 409, cancellation: ct);
            return;
        }
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        ct = leaseCts.Token;

        dbContext.ChangeTracker.Clear();
        context = await CreateChallengeInstanceEndpoint.LoadContextAsync(dbContext, req, userId, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var now = DateTime.UtcNow;
        if (PublicCompetitionGuard.ResolvePlayBlockReason(context.Competition!, now) is { } blockReason)
        {
            await SendStringAsync(blockReason, 403, cancellation: ct);
            return;
        }

        if (!ChallengeInstanceRuntime.IsActive(context.Box, now))
        {
            await SendStringAsync("instance_not_running", 404, cancellation: ct);
            return;
        }

        if (ChallengeInstanceRuntime.IsCoolingDown(context.Box, now, context.Competition!.GameModeType))
        {
            await SendStringAsync("instance_cooldown", 429, cancellation: ct);
            return;
        }

        var requestedExpiry = (context.Box!.ExpiresAt > now ? context.Box.ExpiresAt.Value : now)
            .Add(ChallengeInstanceRuntime.ExtendBy);
        context.Box.ExpiresAt = requestedExpiry <= context.Competition!.EndTime
            ? requestedExpiry
            : context.Competition.EndTime;
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

        await SendAsync(ChallengeInstanceRuntime.BuildResponse(
            context.Box,
            configuration,
            HttpContext,
            DateTime.UtcNow,
            context.Competition!.GameModeType), cancellation: ct);
    }
}

internal readonly record struct ChallengeInstanceError(string Message, int StatusCode);

internal sealed record ChallengeInstanceContext(
    Team? Team,
    Competition? Competition,
    Challenge? Challenge,
    AwdGameBox? Box,
    ChallengeInstanceError? Error)
{
    public static ChallengeInstanceContext Fail(string message, int statusCode)
        => new(null, null, null, null, new ChallengeInstanceError(message, statusCode));
}
