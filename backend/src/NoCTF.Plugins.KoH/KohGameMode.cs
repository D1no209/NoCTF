using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// KoH game mode: territory control via agent polling. Flag submissions are not used.
/// </summary>
public class KohGameMode(
    ApplicationDbContext db,
    IContainerManager containerManager,
    ILogger<KohGameMode> logger) : IGameMode
{
    public GameModeType Type => GameModeType.Koh;

    /// <summary>
    /// Creates one AwdGameBox per challenge as the KoH hill container.
    /// </summary>
    public async Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
    {
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == context.CompetitionId)
            .ToListAsync(cancellationToken);

        foreach (var challenge in challenges)
        {
            var exists = await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .AnyAsync(g => g.CompetitionId == context.CompetitionId
                            && g.ChallengeId == challenge.Id, cancellationToken);

            if (exists) continue;

            string? containerId = null;
            if (challenge.ContainerMode == ChallengeContainerMode.DockerCompose && !string.IsNullOrWhiteSpace(challenge.ComposeYaml))
            {
                try
                {
                    var projectName = string.IsNullOrWhiteSpace(challenge.ComposeProjectName)
                        ? $"noctf-{context.CompetitionId:N}-{challenge.Id:N}"[..42]
                        : challenge.ComposeProjectName;

                    var deployment = await containerManager.ComposeUpAsync(new ComposeConfig(
                        ProjectName: projectName,
                        ComposeYaml: challenge.ComposeYaml,
                        Labels: new Dictionary<string, string>
                        {
                            ["competitionId"] = context.CompetitionId.ToString(),
                            ["challengeId"] = challenge.Id.ToString()
                        }), cancellationToken);

                    containerId = deployment.ProjectName;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "KoH: failed to compose up challenge {ChallengeId}.", challenge.Id);
                }
            }
            else if (!string.IsNullOrEmpty(challenge.ContainerImage))
            {
                try
                {
                    var config = new ContainerConfig(
                        Image: challenge.ContainerImage,
                        Labels: new Dictionary<string, string>
                        {
                            ["competitionId"] = context.CompetitionId.ToString(),
                            ["challengeId"] = challenge.Id.ToString()
                        },
                        PortMappings: challenge.ExposedPort is > 0
                            ? new Dictionary<int, int> { [challenge.ExposedPort.Value] = 0 }
                            : null,
                        OrchestrationJson: challenge.OrchestrationJson);
                    var instance = await containerManager.CreateContainerAsync(config, cancellationToken);
                    containerId = instance.ContainerId;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "KoH: failed to create container for challenge {ChallengeId}.", challenge.Id);
                }
            }

            db.AwdGameBoxes.Add(new AwdGameBox
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                TeamId = Guid.Empty, // KoH hill has no owning team
                ChallengeId = challenge.Id,
                ContainerInstanceId = containerId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>KoH does not use flag submissions.</summary>
    public Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(SubmissionResult.NotImplemented);
}
