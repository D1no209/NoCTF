using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Runs checker containers for each GameBox in a competition round and persists results.
/// </summary>
public class AwdCheckerService(
    ApplicationDbContext db,
    IContainerManager containerManager,
    ILogger<AwdCheckerService> logger)
{
    private const int MaxConcurrency = 4;

    public async Task RunCheckerAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        // Load all game boxes for this competition
        var gameBoxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId)
            .ToListAsync(ct);

        if (gameBoxes.Count == 0)
        {
            logger.LogDebug(
                "RunCheckerAsync: no game boxes found for competition {CompetitionId}.", competitionId);
            return;
        }

        // Load challenges to get CheckerConfig
        var challengeIds = gameBoxes.Select(g => g.ChallengeId).Distinct().ToList();
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => challengeIds.Contains(c.Id))
            .ToListAsync(ct);

        var challengeMap = challenges.ToDictionary(c => c.Id);

        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var results = new List<AwdCheckResult>();
        var resultsLock = new object();

        var tasks = gameBoxes.Select(async gameBox =>
        {
            if (!challengeMap.TryGetValue(gameBox.ChallengeId, out var challenge))
                return;

            var checkerConfig = challenge.CheckerConfig;
            if (checkerConfig?.Image is null)
            {
                logger.LogDebug(
                    "RunCheckerAsync: challenge {ChallengeId} has no checker image, skipping.",
                    gameBox.ChallengeId);
                return;
            }

            await semaphore.WaitAsync(ct);
            try
            {
                var result = await RunSingleCheckerAsync(
                    competitionId, gameBox, checkerConfig, roundNumber, ct);

                lock (resultsLock)
                    results.Add(result);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (results.Count > 0)
        {
            db.AwdCheckResults.AddRange(results);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "RunCheckerAsync: saved {Count} checker results for competition {CompetitionId} round {Round}.",
                results.Count, competitionId, roundNumber);
        }
    }

    private async Task<AwdCheckResult> RunSingleCheckerAsync(
        Guid competitionId,
        AwdGameBox gameBox,
        CheckerConfig checkerConfig,
        int roundNumber,
        CancellationToken ct)
    {
        var targetHost = BuildGameBoxAlias(gameBox.TeamId, gameBox.ChallengeId);
        const int defaultPort = 80;

        var envVars = new Dictionary<string, string>
        {
            ["TARGET_HOST"] = targetHost,
            ["TARGET_PORT"] = defaultPort.ToString(),
            ["ROUND"] = roundNumber.ToString(),
            ["TEAM_ID"] = gameBox.TeamId.ToString()
        };

        var timeout = checkerConfig.TimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(checkerConfig.TimeoutSeconds.Value)
            : TimeSpan.FromSeconds(30);

        var config = new ContainerConfig(
            Image: checkerConfig.Image!,
            Command: checkerConfig.Command,
            EnvironmentVariables: envVars,
            NetworkName: gameBox.OrchestrationNamespace,
            Ttl: timeout
        );

        AwdCheckStatus status;
        string? detail = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout.Add(TimeSpan.FromSeconds(10))); // grace period beyond container TTL

            var runResult = await containerManager.RunContainerAsync(config, cts.Token);

            status = runResult.ExitCode switch
            {
                0 => AwdCheckStatus.Healthy,
                1 => AwdCheckStatus.Down,
                _ => AwdCheckStatus.Error
            };

            if (runResult.ExitCode != 0)
                detail = $"exit={runResult.ExitCode} stderr={runResult.StdErr?.Trim()}";

            logger.LogDebug(
                "Checker for team {TeamId} challenge {ChallengeId} round {Round}: exit={ExitCode} status={Status}.",
                gameBox.TeamId, gameBox.ChallengeId, roundNumber, runResult.ExitCode, status);
        }
        catch (OperationCanceledException)
        {
            status = AwdCheckStatus.Error;
            detail = "Checker timed out.";
            logger.LogWarning(
                "Checker timed out for team {TeamId} challenge {ChallengeId} round {Round}.",
                gameBox.TeamId, gameBox.ChallengeId, roundNumber);
        }
        catch (Exception ex)
        {
            status = AwdCheckStatus.Error;
            detail = ex.Message;
            logger.LogError(ex,
                "Checker failed for team {TeamId} challenge {ChallengeId} round {Round}.",
                gameBox.TeamId, gameBox.ChallengeId, roundNumber);
        }

        return new AwdCheckResult
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = gameBox.TeamId,
            ChallengeId = gameBox.ChallengeId,
            RoundNumber = roundNumber,
            Status = status,
            Detail = detail,
            CheckedAt = DateTime.UtcNow
        };
    }

    private static string BuildGameBoxAlias(Guid teamId, Guid challengeId)
        => $"gamebox-{ShortId(teamId)}-{ShortId(challengeId)}";

    private static string ShortId(Guid id)
        => id.ToString("N")[..8];
}
