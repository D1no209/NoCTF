using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Background service that polls KoH agent /status endpoints and tracks territory control.
/// </summary>
public sealed class KohPollEngine(
    IServiceProvider serviceProvider,
    ILogger<KohPollEngine> logger) : BackgroundService
{
    private const int PollingIntervalSeconds = 5;
    private readonly Dictionary<Guid, DateTime> _lastPollByCompetition = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("KohPollEngine started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollingIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAllActiveCompetitionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "KohPollEngine tick failed.");
            }
        }

        logger.LogInformation("KohPollEngine stopped.");
    }

    private async Task TickAllActiveCompetitionsAsync(CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var agentClient = scope.ServiceProvider.GetRequiredService<KohAgentClient>();
        var scoreEngine = scope.ServiceProvider.GetRequiredService<KohScoreEngine>();

        var now = DateTime.UtcNow;

        var competitions = await db.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.GameModeType == GameModeType.Koh
                     && c.Status == CompetitionStatus.Running
                     && c.StartTime <= now
                     && c.EndTime > now)
            .ToListAsync(ct);

        foreach (var competition in competitions)
        {
            var interval = TimeSpan.FromSeconds(Math.Clamp(competition.PollIntervalSeconds ?? 30, 5, 3600));
            if (_lastPollByCompetition.TryGetValue(competition.Id, out var lastPoll) &&
                now - lastPoll < interval)
            {
                continue;
            }

            try
            {
                await PollCompetitionAsync(scope.ServiceProvider, db, agentClient, scoreEngine, competition, now, ct);
                _lastPollByCompetition[competition.Id] = now;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "KohPollEngine: failed polling competition {CompetitionId}.", competition.Id);
            }
        }
    }

    private async Task PollCompetitionAsync(
        IServiceProvider scopedServices,
        ApplicationDbContext db,
        KohAgentClient agentClient,
        KohScoreEngine scoreEngine,
        Competition competition,
        DateTime now,
        CancellationToken ct)
    {
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competition.Id)
            .ToListAsync(ct);

        var existingBoxCount = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .CountAsync(g => g.CompetitionId == competition.Id, ct);
        if (existingBoxCount < challenges.Count)
        {
            var gameMode = scopedServices.GetRequiredService<KohGameMode>();
            await gameMode.InitializeAsync(new GameContext(
                CompetitionId: competition.Id,
                GameMode: GameModeType.Koh,
                StartTime: competition.StartTime,
                EndTime: competition.EndTime,
                Configuration: new Dictionary<string, string>
                {
                    ["PollIntervalSeconds"] = (competition.PollIntervalSeconds ?? 30).ToString()
                }), ct);
        }

        var gameBoxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competition.Id)
            .ToListAsync(ct);

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competition.Id &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        int controlPoints = competition.ControlPointsPerInterval ?? 10;

        foreach (var challenge in challenges)
        {
            var agentPort = challenge.KohAgentConfig?.Port ?? 8080;
            var apiKey = challenge.KohAgentConfig?.ApiKey;

            var gameBox = gameBoxes.FirstOrDefault(g => g.ChallengeId == challenge.Id);
            if (gameBox is null) continue;

            var host = gameBox.ContainerInstanceId ?? "localhost";

            var status = await agentClient.GetStatusAsync(host, agentPort, apiKey, ct);
            var controllerIdentifier = status?.Success == true ? status.Data?.Identifier : null;

            Guid? controllerTeamId = null;
            if (!string.IsNullOrEmpty(controllerIdentifier))
            {
                if (Guid.TryParse(controllerIdentifier, out var parsedId) && teams.ContainsKey(parsedId))
                    controllerTeamId = parsedId;
                else
                    controllerTeamId = teams.FirstOrDefault(t => t.Value == controllerIdentifier).Key;

                if (controllerTeamId == Guid.Empty)
                    controllerTeamId = null;
            }

            await scoreEngine.UpdateControlAsync(
                competition.Id, challenge.Id,
                controllerTeamId, now, controlPoints, ct);
        }
    }
}
