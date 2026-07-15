using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using System.Text.Json;
using NoCTF.Application.BackgroundTasks;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// KoH game mode: territory control via agent polling. Flag submissions are not used.
/// </summary>
public class KohGameMode(
    ApplicationDbContext db,
    IContainerManager containerManager,
    ILogger<KohGameMode> logger,
    ICompetitionExecutionLease? executionLease = null) : IGameMode
{
    public GameModeType Type => GameModeType.Koh;

    /// <summary>
    /// Creates one AwdGameBox per challenge as the KoH hill container.
    /// </summary>
    public async Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
    {
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == context.CompetitionId && !c.IsDeleting)
            .ToListAsync(cancellationToken);

        var gameBoxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(gameBox =>
                gameBox.CompetitionId == context.CompetitionId &&
                gameBox.TeamId == Guid.Empty)
            .ToListAsync(cancellationToken);
        var gameBoxesByChallenge = gameBoxes.ToDictionary(gameBox => gameBox.ChallengeId);

        if (context.EndTime <= DateTime.UtcNow)
            return;

        foreach (var challenge in challenges)
        {
            var leaseService = executionLease ?? new CompetitionExecutionLease();
            await using var instanceLease = await leaseService.TryAcquireAsync(
                db,
                CompetitionExecutionLeaseKeys.ChallengeInstance(Guid.Empty, challenge.Id),
                context.CompetitionId,
                cancellationToken);
            if (instanceLease is null)
                continue;
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                instanceLease.LostToken);
            var operationCt = operationCts.Token;

            gameBoxesByChallenge.TryGetValue(challenge.Id, out var gameBox);
            if (gameBox?.ContainerInstanceId is not null &&
                (!string.IsNullOrWhiteSpace(gameBox.InternalHost) ||
                 !string.IsNullOrWhiteSpace(gameBox.PublicHost)))
                continue;

            var isCompose = challenge.ContainerMode == ChallengeContainerMode.DockerCompose &&
                            !string.IsNullOrWhiteSpace(challenge.ComposeYaml);
            if (!isCompose && string.IsNullOrWhiteSpace(challenge.ContainerImage))
                continue;

            await using (var preparationLease = await leaseService.TryAcquireAsync(
                db,
                CompetitionExecutionLeaseKeys.RuntimePreparation,
                context.CompetitionId,
                operationCt))
            {
                if (preparationLease is null)
                    continue;
                using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                    operationCt,
                    preparationLease.LostToken);
                var preparationCt = preparationCts.Token;
                var now = DateTime.UtcNow;
                var competitionActive = await db.Competitions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(c =>
                        c.Id == context.CompetitionId &&
                        c.Status == CompetitionStatus.Running &&
                        c.StartTime <= now &&
                        c.EndTime > now,
                        preparationCt);
                var challengeActive = await db.Challenges
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(c =>
                        c.CompetitionId == context.CompetitionId &&
                        c.Id == challenge.Id &&
                        !c.IsDeleting,
                        preparationCt);
                if (!competitionActive || !challengeActive)
                    continue;

                gameBox ??= new AwdGameBox
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = context.CompetitionId,
                    TeamId = Guid.Empty,
                    ChallengeId = challenge.Id,
                    CreatedAt = DateTime.UtcNow
                };
                if (db.Entry(gameBox).State == EntityState.Detached)
                {
                    db.AwdGameBoxes.Add(gameBox);
                    gameBoxesByChallenge[challenge.Id] = gameBox;
                }

                gameBox.RuntimeOperationId ??= Guid.NewGuid();
                gameBox.ExpiresAt = context.EndTime;
                gameBox.LastInstanceActionAt = DateTime.UtcNow;
                await db.SaveChangesAsync(preparationCt);
            }

            if (isCompose)
            {
                var projectName = string.IsNullOrWhiteSpace(challenge.ComposeProjectName)
                    ? $"noctf-{context.CompetitionId:N}-{challenge.Id:N}"[..42]
                    : challenge.ComposeProjectName;
                gameBox.RuntimeKind = "compose";
                gameBox.ComposeProjectName = projectName;
                gameBox.ComposeYaml = challenge.ComposeYaml;
                await db.SaveChangesAsync(operationCt);

                try
                {
                    var deployment = await containerManager.ComposeUpAsync(new ComposeConfig(
                        ProjectName: projectName,
                        ComposeYaml: challenge.ComposeYaml!,
                        Labels: new Dictionary<string, string>
                        {
                            ["competitionId"] = context.CompetitionId.ToString(),
                            ["challengeId"] = challenge.Id.ToString()
                        },
                        Ttl: RemainingTtl(context.EndTime),
                        OrchestrationJson: challenge.OrchestrationJson,
                        OperationId: gameBox.RuntimeOperationId), operationCt);

                    // Persist enough deployment identity before status discovery so a
                    // failed status call can still be recovered or cleaned up.
                    gameBox.ProviderType = deployment.ProviderType;
                    gameBox.ContainerInstanceId = deployment.ProjectName;
                    gameBox.OrchestrationNamespace = deployment.OrchestrationNamespace;
                    gameBox.ExpiresAt = deployment.ExpectedStopAt ?? context.EndTime;
                    await db.SaveChangesAsync(operationCt);

                    var labels = new Dictionary<string, string>
                    {
                        ["competitionId"] = context.CompetitionId.ToString(),
                        ["challengeId"] = challenge.Id.ToString()
                    };
                    var status = await containerManager.GetComposeStatusAsync(deployment.ProjectName, labels, operationCt);
                    var service = status.Services.FirstOrDefault(s =>
                        s.InternalPortMappings?.ContainsKey(challenge.KohAgentConfig?.Port ?? 8080) == true)
                        ?? status.Services.FirstOrDefault();

                    gameBox.ContainerInstanceId = service?.ContainerId ?? deployment.ProjectName;
                    gameBox.ComposeProjectName = deployment.ProjectName;
                    gameBox.ComposeYaml = deployment.ComposeYaml;
                    gameBox.PublicHost = service?.PublicHost ?? deployment.PublicHost;
                    gameBox.EntryUrl = service?.EntryUrl ?? deployment.EntryUrl;
                    gameBox.PortMappingsJson = JsonSerializer.Serialize(service?.PublishedPorts ?? []);
                    gameBox.InternalHost = service?.InternalHost;
                    gameBox.InternalPortMappingsJson = JsonSerializer.Serialize(service?.InternalPortMappings ?? []);
                    gameBox.LastInstanceActionAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(operationCt);
                }
                catch (OperationCanceledException) when (operationCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "KoH: failed to compose up challenge {ChallengeId}.", challenge.Id);
                }
            }
            else if (!string.IsNullOrEmpty(challenge.ContainerImage))
            {
                gameBox.RuntimeKind = "container";
                gameBox.ComposeProjectName = null;
                gameBox.ComposeYaml = null;
                await db.SaveChangesAsync(operationCt);

                try
                {
                    var spec = OrchestrationSpecSerializer.Read(challenge.OrchestrationJson);
                    var image = string.IsNullOrWhiteSpace(spec.Image) ? challenge.ContainerImage : spec.Image;
                    var exposedPort = spec.ExposedPort ?? challenge.ExposedPort;
                    var config = new ContainerConfig(
                        Image: image!,
                        Command: spec.Command,
                        EnvironmentVariables: new Dictionary<string, string>(spec.Environment, StringComparer.Ordinal),
                        Labels: new Dictionary<string, string>
                        {
                            ["competitionId"] = context.CompetitionId.ToString(),
                            ["challengeId"] = challenge.Id.ToString()
                        },
                        PortMappings: exposedPort is > 0
                            ? new Dictionary<int, int> { [exposedPort.Value] = 0 }
                            : null,
                        Ttl: RemainingTtl(context.EndTime),
                        Entrypoint: spec.Entrypoint,
                        OrchestrationJson: challenge.OrchestrationJson,
                        OperationId: gameBox.RuntimeOperationId);
                    var instance = await containerManager.CreateContainerAsync(config, operationCt);
                    gameBox.ProviderType = instance.ProviderType;
                    gameBox.ContainerInstanceId = instance.ContainerId;
                    gameBox.OrchestrationNamespace = instance.OrchestrationNamespace;
                    gameBox.PublicHost = instance.PublicHost;
                    gameBox.EntryUrl = instance.EntryUrl;
                    gameBox.PortMappingsJson = JsonSerializer.Serialize(instance.PortMappings);
                    gameBox.InternalHost = instance.InternalHost;
                    gameBox.InternalPortMappingsJson = JsonSerializer.Serialize(instance.InternalPortMappings ?? []);
                    gameBox.ExpiresAt = instance.ExpectedStopAt ?? context.EndTime;
                    gameBox.LastInstanceActionAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(operationCt);
                }
                catch (OperationCanceledException) when (operationCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "KoH: failed to create container for challenge {ChallengeId}.", challenge.Id);
                }
            }

        }
    }

    private static TimeSpan RemainingTtl(DateTime endTime)
        => endTime > DateTime.UtcNow
            ? endTime - DateTime.UtcNow
            : TimeSpan.FromSeconds(1);

    public Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>KoH does not use flag submissions.</summary>
    public Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(SubmissionResult.NotImplemented);
}
