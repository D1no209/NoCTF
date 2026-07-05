using System.Security.Claims;
using System.Text.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Application.CompetitionModes;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class PenetrationTemplateTopologyRequest
{
    public Guid TemplateId { get; set; }
}

public class PenetrationCompetitionTopologyRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
}

public class PenetrationAdminInstanceListRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? ChallengeId { get; set; }
    public Guid? TeamId { get; set; }
}

public class PenetrationAdminInstanceRequest
{
    public Guid CompetitionId { get; set; }
    public Guid InstanceId { get; set; }
}

internal static class PenetrationAdminEndpointRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
        => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId);

    public static async Task<(string? TypeId, int? ErrorStatus, string? ErrorCode)> LoadChallengeTypeAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompetitionId == competitionId && c.Id == challengeId, ct);
        return challenge is null ? (null, 404, "challenge_not_found") : (challenge.TypeId, null, null);
    }

    public static ChallengeFeatureContext BuildContext(
        Guid competitionId,
        Guid challengeId,
        Guid userId,
        string typeId,
        string featureKey,
        HttpContext httpContext,
        object? payload = null)
        => new(
            CompetitionId: competitionId,
            ChallengeId: challengeId,
            TeamId: null,
            UserId: userId,
            TypeId: typeId,
            FeatureKey: featureKey,
            PayloadJson: payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOptions),
            IpAddress: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    public static int StatusFromException(InvalidOperationException ex)
        => ex.Message switch
        {
            "challenge_template_not_found" or "challenge_not_found" or "instance_not_found" => 404,
            "instance_cooldown" => 429,
            "active_instances_exist" or "instance_busy" or "reset_limit_exceeded" => 409,
            _ => 400
        };
}

public class GetPenetrationTemplateTopologyEndpoint(
    ApplicationDbContext db,
    IChallengeAdminFeatureRegistry registry)
    : Endpoint<PenetrationTemplateTopologyRequest>
{
    public override void Configure()
    {
        Get("/api/admin/challenges/{templateId}/penetration-topology");
        Roles("Admin");
    }

    public override async Task HandleAsync(PenetrationTemplateTopologyRequest req, CancellationToken ct)
    {
        var template = await db.ChallengeTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
        {
            await SendStringAsync("challenge_template_not_found", 404, cancellation: ct);
            return;
        }

        var provider = registry.FindProvider(template.TypeId);
        if (provider is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        try
        {
            var userId = PenetrationAdminEndpointRuntime.TryGetUserId(User, out var id) ? id : Guid.Empty;
            var result = await provider.HandleAsync(
                PenetrationAdminEndpointRuntime.BuildContext(Guid.Empty, req.TemplateId, userId, template.TypeId, "penetration.template.topology.get", HttpContext),
                ct);
            await SendAsync(result.Data, result.StatusCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendStringAsync(ex.Message, PenetrationAdminEndpointRuntime.StatusFromException(ex), cancellation: ct);
        }
    }
}

public class UpdatePenetrationTemplateTopologyEndpoint(
    ApplicationDbContext db,
    IChallengeAdminFeatureRegistry registry)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Put("/api/admin/challenges/{templateId}/penetration-topology");
        Roles("Admin");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var templateId = Route<Guid>("templateId");
        var template = await db.ChallengeTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
        {
            await SendStringAsync("challenge_template_not_found", 404, cancellation: ct);
            return;
        }

        var provider = registry.FindProvider(template.TypeId);
        if (provider is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        using var reader = new StreamReader(HttpContext.Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var userId = PenetrationAdminEndpointRuntime.TryGetUserId(User, out var id) ? id : Guid.Empty;

        try
        {
            var result = await provider.HandleAsync(
                PenetrationAdminEndpointRuntime.BuildContext(Guid.Empty, templateId, userId, template.TypeId, "penetration.template.topology.update", HttpContext, JsonSerializer.Deserialize<JsonElement>(payload)),
                ct);
            await SendAsync(result.Data, result.StatusCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendStringAsync(ex.Message, PenetrationAdminEndpointRuntime.StatusFromException(ex), cancellation: ct);
        }
    }
}

public class GetPenetrationCompetitionTopologyEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    IChallengeAdminFeatureRegistry registry)
    : Endpoint<PenetrationCompetitionTopologyRequest>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(PenetrationCompetitionTopologyRequest req, CancellationToken ct)
        => await HandleTopologyAsync(req, "penetration.competition.topology.get", null, ct);

    protected async Task HandleTopologyAsync(PenetrationCompetitionTopologyRequest req, string featureKey, object? payload, CancellationToken ct)
    {
        if (!PenetrationAdminEndpointRuntime.TryGetUserId(User, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (!await permissions.CanManageCompetitionAsync(userId, req.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var (typeId, status, code) = await PenetrationAdminEndpointRuntime.LoadChallengeTypeAsync(db, req.CompetitionId, req.ChallengeId, ct);
        if (status.HasValue)
        {
            await SendStringAsync(code!, status.Value, cancellation: ct);
            return;
        }

        var provider = registry.FindProvider(typeId!);
        if (provider is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        try
        {
            var result = await provider.HandleAsync(
                PenetrationAdminEndpointRuntime.BuildContext(req.CompetitionId, req.ChallengeId, userId, typeId!, featureKey, HttpContext, payload),
                ct);
            await SendAsync(result.Data, result.StatusCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendStringAsync(ex.Message, PenetrationAdminEndpointRuntime.StatusFromException(ex), cancellation: ct);
        }
    }
}

public class UpdatePenetrationCompetitionTopologyEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    IChallengeAdminFeatureRegistry registry)
    : GetPenetrationCompetitionTopologyEndpoint(db, permissions, registry)
{
    public override void Configure()
    {
        Put("/api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(PenetrationCompetitionTopologyRequest req, CancellationToken ct)
    {
        using var reader = new StreamReader(HttpContext.Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        await HandleTopologyAsync(
            req,
            "penetration.competition.topology.update",
            JsonSerializer.Deserialize<JsonElement>(payload),
            ct);
    }
}

public class ListPenetrationAdminInstancesEndpoint(
    ICompetitionPermissionService permissions,
    IChallengeAdminFeatureRegistry registry)
    : Endpoint<PenetrationAdminInstanceListRequest>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/penetration/instances");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(PenetrationAdminInstanceListRequest req, CancellationToken ct)
    {
        if (!PenetrationAdminEndpointRuntime.TryGetUserId(User, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }
        if (!await permissions.CanManageCompetitionAsync(userId, req.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var provider = registry.FindProvider("penetration");
        if (provider is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        try
        {
            var result = await provider.HandleAsync(
                PenetrationAdminEndpointRuntime.BuildContext(req.CompetitionId, Guid.Empty, userId, "penetration", "penetration.admin.instances.list", HttpContext, new { req.ChallengeId, req.TeamId }),
                ct);
            await SendAsync(result.Data, result.StatusCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendStringAsync(ex.Message, PenetrationAdminEndpointRuntime.StatusFromException(ex), cancellation: ct);
        }
    }
}

public class GetPenetrationAdminInstanceEndpoint(
    ICompetitionPermissionService permissions,
    IChallengeAdminFeatureRegistry registry)
    : Endpoint<PenetrationAdminInstanceRequest>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/penetration/instances/{instanceId}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(PenetrationAdminInstanceRequest req, CancellationToken ct)
        => await HandleInstanceAsync(req, "penetration.admin.instance.get", ct);

    protected async Task HandleInstanceAsync(PenetrationAdminInstanceRequest req, string featureKey, CancellationToken ct)
    {
        if (!PenetrationAdminEndpointRuntime.TryGetUserId(User, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }
        if (!await permissions.CanManageCompetitionAsync(userId, req.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var provider = registry.FindProvider("penetration");
        if (provider is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        try
        {
            var result = await provider.HandleAsync(
                PenetrationAdminEndpointRuntime.BuildContext(req.CompetitionId, Guid.Empty, userId, "penetration", featureKey, HttpContext, new { req.InstanceId }),
                ct);
            await SendAsync(result.Data, result.StatusCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendStringAsync(ex.Message, PenetrationAdminEndpointRuntime.StatusFromException(ex), cancellation: ct);
        }
    }
}

public class ResetPenetrationAdminInstanceEndpoint(
    ICompetitionPermissionService permissions,
    IChallengeAdminFeatureRegistry registry)
    : GetPenetrationAdminInstanceEndpoint(permissions, registry)
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/penetration/instances/{instanceId}/reset");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(PenetrationAdminInstanceRequest req, CancellationToken ct)
        => await HandleInstanceAsync(req, "penetration.admin.instance.reset", ct);
}

public class DestroyPenetrationAdminInstanceEndpoint(
    ICompetitionPermissionService permissions,
    IChallengeAdminFeatureRegistry registry)
    : GetPenetrationAdminInstanceEndpoint(permissions, registry)
{
    public override void Configure()
    {
        Delete("/api/admin/competitions/{competitionId}/penetration/instances/{instanceId}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(PenetrationAdminInstanceRequest req, CancellationToken ct)
        => await HandleInstanceAsync(req, "penetration.admin.instance.destroy", ct);
}
