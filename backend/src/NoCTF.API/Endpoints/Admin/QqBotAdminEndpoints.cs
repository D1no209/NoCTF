using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Application.QqBot;
using NoCTF.Core;

namespace NoCTF.API.Endpoints.Admin;

public sealed class GetQqBotOverviewEndpoint(IQqBotAdministrationService service)
    : EndpointWithoutRequest<QqBotAdminOverview>
{
    public override void Configure() { Get("/api/admin/qqbot"); Roles("Admin"); }
    public override async Task HandleAsync(CancellationToken ct) => await SendAsync(await service.GetOverviewAsync(ct), cancellation: ct);
}

public sealed class UpdateQqBotSettingsEndpoint(IQqBotAdministrationService service)
    : Endpoint<QqBotGlobalSettingsUpdate, QqBotGlobalSettingsView>, IAuditableEndpoint
{
    public override void Configure() { Put("/api/admin/qqbot/settings"); Roles("Admin"); }
    public override async Task HandleAsync(QqBotGlobalSettingsUpdate req, CancellationToken ct)
        => await SendAsync(await service.UpdateGlobalSettingsAsync(req, ct), cancellation: ct);
}

public sealed class UpsertQqBotAgentEndpoint(IQqBotAdministrationService service)
    : Endpoint<QqBotAgentUpdate, QqBotAgentView>, IAuditableEndpoint
{
    public override void Configure() { Put("/api/admin/qqbot/agents"); Roles("Admin"); }
    public override async Task HandleAsync(QqBotAgentUpdate req, CancellationToken ct)
        => await SendAsync(await service.UpsertAgentAsync(req, ct), cancellation: ct);
}

public sealed class AuthorizeQqBotGroupEndpoint(IQqBotAdministrationService service)
    : Endpoint<QqBotGroupAuthorizationUpdate, QqBotGroupView>, IAuditableEndpoint
{
    public override void Configure() { Put("/api/admin/qqbot/groups/{groupId}/authorization"); Roles("Admin"); }
    public override async Task HandleAsync(QqBotGroupAuthorizationUpdate req, CancellationToken ct)
        => await SendAsync(await service.SetGroupAuthorizationAsync(Route<Guid>("groupId"), req.IsAuthorized, ct), cancellation: ct);
}

public sealed class GetAvailableQqBotGroupsEndpoint(NoCTF.Infrastructure.ApplicationDbContext db)
    : EndpointWithoutRequest<IReadOnlyList<QqBotGroupView>>
{
    public override void Configure() { Get("/api/admin/competitions/{competitionId}/qqbot/groups/available"); Roles("Admin"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var groups = await db.QqBotGroups.AsNoTracking()
            .Where(item => item.IsPresent && item.IsAuthorized)
            .OrderBy(item => item.GroupName)
            .Select(item => new QqBotGroupView(item.Id, item.AgentId, item.GroupId, item.GroupName,
                item.IsPresent, item.IsAuthorized, item.LastSeenAt))
            .ToListAsync(ct);
        await SendAsync(groups, cancellation: ct);
    }
}

public sealed class GetCompetitionQqBotConfigEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions) : EndpointWithoutRequest<CompetitionQqBotConfigurationView>
{
    public override void Configure() { Get("/api/admin/competitions/{competitionId}/qqbot"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        await SendAsync(await service.GetCompetitionConfigurationAsync(id, ct), cancellation: ct);
    }
}

public sealed class UpdateCompetitionQqBotConfigEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions,
    NoCTF.Infrastructure.ApplicationDbContext db) : Endpoint<CompetitionQqBotConfigurationUpdate, CompetitionQqBotConfigurationView>, IAuditableEndpoint
{
    public override void Configure() { Put("/api/admin/competitions/{competitionId}/qqbot"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(CompetitionQqBotConfigurationUpdate req, CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        if (!User.IsInRole("Admin"))
        {
            var existingGroupIds = await db.CompetitionQqBotGroupBindings
                .IgnoreQueryFilters()
                .Where(item => item.CompetitionId == id)
                .Select(item => item.GroupId)
                .ToListAsync(ct);
            if (req.GroupBindings.Any(item => !existingGroupIds.Contains(item.GroupId)))
            {
                await SendForbiddenAsync(ct);
                return;
            }
        }
        await SendAsync(await service.UpdateCompetitionConfigurationAsync(id, req, ct), cancellation: ct);
    }
}

public sealed class GetCompetitionQqBotTemplatesEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions) : EndpointWithoutRequest<IReadOnlyList<QqBotTemplateView>>
{
    public override void Configure() { Get("/api/admin/competitions/{competitionId}/qqbot/templates"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        await SendAsync(await service.GetTemplatesAsync(id, ct), cancellation: ct);
    }
}

public sealed class UpsertCompetitionQqBotTemplateEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions) : Endpoint<QqBotTemplateUpdate, QqBotTemplateView>, IAuditableEndpoint
{
    public override void Configure() { Put("/api/admin/competitions/{competitionId}/qqbot/templates"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(QqBotTemplateUpdate req, CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        if (req.CompetitionId != id) { await SendStringAsync("qqbot_template_scope_invalid", 400, cancellation: ct); return; }
        if (!TryUserId(out var userId)) { await SendUnauthorizedAsync(ct); return; }
        await SendAsync(await service.UpsertTemplateAsync(req, userId, ct), cancellation: ct);
    }
    private bool TryUserId(out Guid id) => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out id);
}

public sealed class PreviewCompetitionQqBotMessageEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions) : Endpoint<QqBotPreviewRequest, QqBotPreviewResult>
{
    public override void Configure() { Post("/api/admin/competitions/{competitionId}/qqbot/preview"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(QqBotPreviewRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (req.CompetitionId != id || !await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        await SendAsync(await service.PreviewAsync(req, ct), cancellation: ct);
    }
}

public sealed class SendCompetitionQqBotNotificationEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions) : Endpoint<QqBotManualNotificationRequest, QqBotQueuedNotification>, IAuditableEndpoint
{
    public override void Configure() { Post("/api/admin/competitions/{competitionId}/qqbot/notifications"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(QqBotManualNotificationRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (req.CompetitionId != id || !await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) { await SendUnauthorizedAsync(ct); return; }
        await SendAsync(await service.QueueManualNotificationAsync(req, userId, ct), 202, ct);
    }
}

public sealed class QqBotLogRequest
{
    public Guid? CompetitionId { get; set; }
    public QqBotEventType? EventType { get; set; }
    public QqBotDeliveryStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class GetCompetitionQqBotLogsEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions) : Endpoint<QqBotLogRequest, QqBotDeliveryLogPage>
{
    public override void Configure() { Get("/api/admin/competitions/{competitionId}/qqbot/logs"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(QqBotLogRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct)) { await SendForbiddenAsync(ct); return; }
        await SendAsync(await service.GetDeliveryLogsAsync(new QqBotDeliveryLogFilter(id, req.EventType, req.Status, req.From, req.To, req.Page, req.PageSize), ct), cancellation: ct);
    }
}

public sealed class RetryCompetitionQqBotDeliveryEndpoint(
    IQqBotAdministrationService service,
    ICompetitionPermissionService permissions,
    NoCTF.Infrastructure.ApplicationDbContext db) : EndpointWithoutRequest<QqBotRetryResult>, IAuditableEndpoint
{
    public override void Configure() { Post("/api/admin/competitions/{competitionId}/qqbot/deliveries/{deliveryId}/retry"); Roles("Admin", "Organizer"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var deliveryId = Route<Guid>("deliveryId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct)) { await SendForbiddenAsync(ct); return; }
        var belongs = await db.QqBotDeliveries.IgnoreQueryFilters().AnyAsync(item => item.Id == deliveryId && item.CompetitionId == competitionId, ct);
        if (!belongs) { await SendNotFoundAsync(ct); return; }
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) { await SendUnauthorizedAsync(ct); return; }
        await SendAsync(await service.RetryDeliveryAsync(deliveryId, userId, ct), 202, ct);
    }
}
