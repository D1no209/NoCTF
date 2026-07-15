using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.API.Permissions;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class RestartStaticChallengeContainerEndpoint(
    ApplicationDbContext dbContext,
    IContainerManager containerManager,
    ICompetitionPermissionService permissions,
    ICompetitionExecutionLease executionLease)
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
            .FirstOrDefaultAsync(c =>
                c.Id == challengeId &&
                c.CompetitionId == competitionId &&
                !c.IsDeleting,
                ct);
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

        await using var transitionLease = await executionLease.TryAcquireAsync(
            dbContext,
            CompetitionExecutionLeaseKeys.ChallengeInstance(Guid.Empty, challengeId),
            competitionId,
            ct);
        if (transitionLease is null)
        {
            await SendStringAsync("instance_transition_in_progress", 409, cancellation: ct);
            return;
        }
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        ct = leaseCts.Token;

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
            Competitions.ChallengeInstanceRuntime.ClearContainer(box);
            box.LastInstanceActionAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }

        DateTime competitionEnd;
        await using (var preparationLease = await executionLease.TryAcquireAsync(
            dbContext,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            competitionId,
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
            challenge = await dbContext.Challenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == challengeId &&
                    c.CompetitionId == competitionId &&
                    !c.IsDeleting,
                    preparationCt);
            if (challenge is null)
            {
                await SendNotFoundAsync(preparationCt);
                return;
            }

            var competition = await dbContext.Competitions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == competitionId, preparationCt);
            if (competition is null ||
                competition.Status == CompetitionStatus.Finished ||
                competition.EndTime <= DateTime.UtcNow)
            {
                await SendStringAsync("competition_ended", 409, cancellation: preparationCt);
                return;
            }
            competitionEnd = competition.EndTime;

            box ??= new AwdGameBox
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = Guid.Empty,
                ChallengeId = challengeId,
                CreatedAt = DateTime.UtcNow
            };
            if (dbContext.Entry(box).State == EntityState.Detached)
                dbContext.AwdGameBoxes.Add(box);
            box.RuntimeOperationId ??= Guid.NewGuid();
            await dbContext.SaveChangesAsync(preparationCt);
        }

        var remaining = competitionEnd - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            box.RuntimeOperationId = null;
            await dbContext.SaveChangesAsync(ct);
            await SendStringAsync("competition_ended", 409, cancellation: ct);
            return;
        }
        var containerConfig = Competitions.CreateChallengeInstanceEndpoint
            .BuildContainerConfig(challenge, Guid.Empty, operationId: box.RuntimeOperationId) with
        {
            Ttl = remaining
        };
        var instance = await containerManager.CreateContainerAsync(
            containerConfig,
            ct);
        box.ContainerInstanceId = instance.ContainerId;
        box.ProviderType = instance.ProviderType;
        box.PublicHost = instance.PublicHost;
        box.EntryUrl = instance.EntryUrl;
        box.OrchestrationNamespace = instance.OrchestrationNamespace;
        box.PortMappingsJson = Competitions.ChallengeInstanceRuntime.WritePorts(instance.PortMappings);
        box.RuntimeKind = "container";
        box.ComposeProjectName = null;
        box.ComposeYaml = null;
        box.InternalHost = instance.InternalHost;
        box.InternalPortMappingsJson = Competitions.ChallengeInstanceRuntime.WritePorts(
            instance.InternalPortMappings ?? []);
        box.ExpiresAt = competitionEnd;
        box.LastInstanceActionAt = DateTime.UtcNow;
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
