using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class RestartStaticChallengeContainerEndpoint(ApplicationDbContext dbContext, IContainerManager containerManager)
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
            CreatedAt = DateTime.UtcNow,
        });
        CompetitionLogWriter.Add(
            dbContext,
            competitionId,
            "container.created",
            $"Static container was started for challenge {challenge.Title}.",
            challengeId: challengeId,
            metadata: new { instance.ContainerId, instance.PortMappings });
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(new Competitions.ChallengeInstanceResponse
        {
            ContainerId = instance.ContainerId,
            Ports = instance.PortMappings,
            Status = instance.Status,
        }, cancellation: ct);
    }
}
