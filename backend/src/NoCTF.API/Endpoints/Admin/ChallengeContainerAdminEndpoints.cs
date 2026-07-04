using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class RestartStaticChallengeContainerEndpoint(ApplicationDbContext dbContext, IContainerManager containerManager, ICompetitionPermissionService permissions)
    : EndpointWithoutRequest<Competitions.ChallengeInstanceResponse>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/challenges/{challengeId}/container/restart");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var challengeId = Route<Guid>("challengeId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var challenge = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.CompetitionId == competitionId, ct);
        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (challenge.DeploymentType != ChallengeDeploymentType.StaticContainer)
        {
            await SendStringAsync("not_static_container", 400, cancellation: ct);
            return;
        }

        var box = await dbContext.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.CompetitionId == competitionId && g.TeamId == Guid.Empty && g.ChallengeId == challengeId, ct);
        if (box?.ContainerInstanceId is not null)
        {
            CompetitionLogWriter.Add(
                dbContext,
                competitionId,
                "container.destroyed",
                $"Existing static container was destroyed before restart for challenge {challenge.Title}.",
                challengeId: challengeId,
                metadata: new { box.ContainerInstanceId });
            AuditLogWriter.Add(
                dbContext,
                HttpContext,
                "container.instance.destroyed",
                "Container",
                box.ContainerInstanceId,
                new
                {
                    competitionId,
                    challengeId,
                    teamId = Guid.Empty,
                    containerId = box.ContainerInstanceId,
                    reason = "static_container_restart"
                });
            await Competitions.CreateChallengeInstanceEndpoint.DestroyBoxAsync(box, containerManager, ct);
            dbContext.AwdGameBoxes.Remove(box);
        }

        var instance = await containerManager.CreateContainerAsync(
            Competitions.CreateChallengeInstanceEndpoint.BuildContainerConfig(challenge, Guid.Empty),
            ct);
        dbContext.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = Guid.Empty,
            ChallengeId = challengeId,
            ContainerInstanceId = instance.ContainerId,
            ProviderType = instance.ProviderType,
            PublicHost = instance.PublicHost,
            EntryUrl = instance.EntryUrl,
            OrchestrationNamespace = instance.OrchestrationNamespace,
            PortMappingsJson = Competitions.ChallengeInstanceRuntime.WritePorts(instance.PortMappings),
            CreatedAt = DateTime.UtcNow,
        });
        CompetitionLogWriter.Add(
            dbContext,
            competitionId,
            "container.created",
            $"Static container was started for challenge {challenge.Title}.",
            challengeId: challengeId,
            metadata: new { instance.ContainerId, instance.PortMappings });
        AuditLogWriter.Add(
            dbContext,
            HttpContext,
            "container.instance.created",
            "Container",
            instance.ContainerId,
            new
            {
                competitionId,
                challengeId,
                teamId = Guid.Empty,
                containerId = instance.ContainerId,
                ports = instance.PortMappings,
                status = instance.Status,
                reason = "static_container_restart"
            });
        await dbContext.SaveChangesAsync(ct);

        var publicHost = ChallengeTemplateRequestRules.CleanOptional(instance.PublicHost);
        var addressHost = string.IsNullOrWhiteSpace(publicHost) ? null : FormatHost(publicHost);
        var portAddresses = instance.PortMappings.Values
            .Where(port => port > 0)
            .Select(port => addressHost is null ? port.ToString() : $"{addressHost}:{port}")
            .ToList();
        var firstPortAddress = portAddresses.FirstOrDefault();
        await SendAsync(new Competitions.ChallengeInstanceResponse
        {
            ContainerId = instance.ContainerId,
            Ports = instance.PortMappings,
            AccessHost = publicHost ?? string.Empty,
            EntryUrl = instance.EntryUrl,
            Address = instance.EntryUrl ?? firstPortAddress,
            Addresses = !string.IsNullOrWhiteSpace(instance.EntryUrl)
                ? [instance.EntryUrl]
                : portAddresses,
            Status = instance.Status,
        }, cancellation: ct);
    }

    private static string FormatHost(string host)
        => host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
}
