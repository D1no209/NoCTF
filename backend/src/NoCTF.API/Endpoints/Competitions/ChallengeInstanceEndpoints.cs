using System.Security.Claims;
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
    public string ContainerId { get; set; } = string.Empty;
    public Dictionary<int, int> Ports { get; set; } = [];
    public string Status { get; set; } = string.Empty;
}

public class CreateChallengeInstanceEndpoint(ApplicationDbContext dbContext, IContainerManager containerManager)
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

        var team = await dbContext.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.CompetitionId == req.Id && tm.UserId == userId)
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                tm => tm.TeamId,
                t => t.Id,
                (tm, t) => t)
            .FirstOrDefaultAsync(ct);
        if (team is null)
        {
            await SendStringAsync("no_team", 400, cancellation: ct);
            return;
        }

        if (team.RegistrationStatus != TeamRegistrationStatus.Approved)
        {
            await SendStringAsync("team_not_approved", 403, cancellation: ct);
            return;
        }

        if (team.IsBanned)
        {
            await SendStringAsync("team_banned", 403, cancellation: ct);
            return;
        }

        var challenge = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.ChallengeId && c.CompetitionId == req.Id, ct);
        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (challenge.DeploymentType != ChallengeDeploymentType.DynamicContainer)
        {
            await SendStringAsync("not_dynamic_container", 400, cancellation: ct);
            return;
        }

        var existing = await dbContext.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.CompetitionId == req.Id && g.TeamId == team.Id && g.ChallengeId == req.ChallengeId, ct);
        if (existing?.ContainerInstanceId is not null)
        {
            CompetitionLogWriter.Add(
                dbContext,
                req.Id,
                "container.destroyed",
                $"Existing dynamic container was destroyed before recreation for challenge {challenge.Title}.",
                teamId: team.Id,
                userId: userId,
                challengeId: req.ChallengeId,
                metadata: new { existing.ContainerInstanceId });
            await DestroyBoxAsync(existing, containerManager, ct);
            dbContext.AwdGameBoxes.Remove(existing);
        }

        var dynamicFlag = await UpsertDynamicFlagAsync(challenge, team.Id, ct);
        var instance = await containerManager.CreateContainerAsync(BuildContainerConfig(challenge, team.Id, dynamicFlag), ct);
        var box = new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = req.Id,
            TeamId = team.Id,
            ChallengeId = req.ChallengeId,
            ContainerInstanceId = instance.ContainerId,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.AwdGameBoxes.Add(box);
        CompetitionLogWriter.Add(
            dbContext,
            req.Id,
            "container.created",
            $"Dynamic container was created for challenge {challenge.Title}.",
            teamId: team.Id,
            userId: userId,
            challengeId: req.ChallengeId,
            metadata: new { instance.ContainerId, instance.PortMappings, flagEnv = dynamicFlag.EnvironmentVariable });
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(new ChallengeInstanceResponse
        {
            ContainerId = instance.ContainerId,
            Ports = instance.PortMappings,
            Status = instance.Status,
        }, cancellation: ct);
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
            Ttl: TimeSpan.FromHours(2));
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
            new Dictionary<int, int>(),
            "running",
            DateTime.UtcNow);
        await containerManager.DestroyContainerAsync(instance, ct);
    }
}
