using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Leaderboard;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using System.Text.Json;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Background service that polls KoH agent /status endpoints and tracks territory control.
/// </summary>
public sealed class KohPollEngine(
    IServiceProvider serviceProvider,
    ILogger<KohPollEngine> logger,
    IConfiguration configuration) : BackgroundService
{
    private const int PollingIntervalSeconds = 5;
    private const string ScheduleEngineKey = "koh-poll";
    private const string CompletionEngineKey = "koh-poll:completed";
    private readonly int _maxConcurrentPolls = Math.Clamp(
        configuration.GetValue("Koh:MaxConcurrentPolls", 8),
        1,
        64);

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
        var executionLease = scope.ServiceProvider.GetRequiredService<ICompetitionExecutionLease>();

        var now = DateTime.UtcNow;

        var competitions = await db.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.GameModeType == GameModeType.Koh
                     && c.Status == CompetitionStatus.Running
                     && c.StartTime <= now)
            .ToListAsync(ct);

        foreach (var competition in competitions)
        {
            var interval = TimeSpan.FromSeconds(Math.Clamp(competition.PollIntervalSeconds ?? 30, 5, 3600));
            await using var lease = await executionLease.TryAcquireAsync(db, "koh-poll", competition.Id, ct);
            if (lease is null)
                continue;

            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
            var leaseCt = leaseCts.Token;

            var engineStates = await db.CompetitionEngineStates
                .IgnoreQueryFilters()
                .Where(s =>
                    s.CompetitionId == competition.Id &&
                    (s.EngineKey == ScheduleEngineKey || s.EngineKey == CompletionEngineKey))
                .ToListAsync(leaseCt);
            var scheduleState = engineStates.FirstOrDefault(state => state.EngineKey == ScheduleEngineKey);
            var completionState = engineStates.FirstOrDefault(state => state.EngineKey == CompletionEngineKey);
            DateTime pollTimestamp;
            if (scheduleState?.LastExecutedAt is { } scheduledAt &&
                (completionState?.LastExecutedAt is null || completionState.LastExecutedAt < scheduledAt))
            {
                // Resume the exact logical poll slot. KoH scoring keys include
                // this timestamp, so a crash after a partial commit is replayed
                // idempotently instead of losing or double-awarding an interval.
                pollTimestamp = scheduledAt;
            }
            else
            {
                if (competition.EndTime <= now)
                    continue;
                if (scheduleState?.LastExecutedAt is { } lastPoll && now - lastPoll < interval)
                    continue;

                scheduleState ??= new CompetitionEngineState
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competition.Id,
                    EngineKey = ScheduleEngineKey
                };
                if (db.Entry(scheduleState).State == EntityState.Detached)
                    db.CompetitionEngineStates.Add(scheduleState);
                pollTimestamp = now;
                scheduleState.LastExecutedAt = pollTimestamp;
                scheduleState.UpdatedAt = now;
                await db.SaveChangesAsync(leaseCt);
            }

            try
            {
                await PollCompetitionAsync(
                    scope.ServiceProvider,
                    db,
                    agentClient,
                    scoreEngine,
                    competition,
                    pollTimestamp,
                    leaseCt);

                completionState ??= new CompetitionEngineState
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competition.Id,
                    EngineKey = CompletionEngineKey
                };
                if (db.Entry(completionState).State == EntityState.Detached)
                    db.CompetitionEngineStates.Add(completionState);
                completionState.LastExecutedAt = pollTimestamp;
                completionState.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(leaseCt);
            }
            catch (OperationCanceledException) when (leaseCt.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "KohPollEngine: failed polling competition {CompetitionId}.", competition.Id);
            }
        }
    }

    internal async Task PollCompetitionAsync(
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
            .Where(c => c.CompetitionId == competition.Id && !c.IsDeleting)
            .ToListAsync(ct);

        var gameBoxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competition.Id && g.TeamId == Guid.Empty)
            .ToListAsync(ct);
        var readyChallengeIds = gameBoxes
            .Where(gameBox =>
                gameBox.ContainerInstanceId is not null &&
                (gameBox.InternalHost is not null || gameBox.PublicHost is not null))
            .Select(gameBox => gameBox.ChallengeId)
            .ToHashSet();
        var requiresInitialization = challenges.Any(challenge =>
            HasRuntimeConfiguration(challenge) &&
            !readyChallengeIds.Contains(challenge.Id));
        if (requiresInitialization)
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
            gameBoxes = await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .Where(g => g.CompetitionId == competition.Id && g.TeamId == Guid.Empty)
                .ToListAsync(ct);
        }

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competition.Id &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        int controlPoints = competition.ControlPointsPerInterval ?? 10;

        var boxesByChallenge = gameBoxes
            .GroupBy(box => box.ChallengeId)
            .ToDictionary(group => group.Key, group => group.First());
        var pollTargets = new List<PollTarget>(challenges.Count);
        foreach (var challenge in challenges)
        {
            var agentPort = challenge.KohAgentConfig?.Port ?? 8080;
            var apiKey = challenge.KohAgentConfig?.ApiKey;

            var gameBox = boxesByChallenge.GetValueOrDefault(challenge.Id);
            if (gameBox is null) continue;

            var internalPorts = ReadPorts(gameBox.InternalPortMappingsJson);
            var publishedPorts = ReadPorts(gameBox.PortMappingsJson);
            var host = gameBox.InternalHost ?? gameBox.PublicHost;
            var reachablePort = gameBox.InternalHost is not null
                ? internalPorts.GetValueOrDefault(agentPort, agentPort)
                : publishedPorts.GetValueOrDefault(agentPort, agentPort);
            if (string.IsNullOrWhiteSpace(host))
            {
                logger.LogWarning(
                    "KoH game box {GameBoxId} has no routable endpoint for challenge {ChallengeId}.",
                    gameBox.Id,
                    challenge.Id);
                continue;
            }

            pollTargets.Add(new PollTarget(challenge.Id, host, reachablePort, apiKey));
        }

        var pollResults = new System.Collections.Concurrent.ConcurrentDictionary<Guid, PollResult>();
        await Parallel.ForEachAsync(
            pollTargets,
            new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentPolls, CancellationToken = ct },
            async (target, token) =>
            {
                var status = await agentClient.PollStatusAsync(target.Host, target.Port, target.ApiKey, token);
                pollResults[target.ChallengeId] = new PollResult(
                    status.IsAuthoritative,
                    ResolveControllerTeamId(status.Identifier, teams));
            });

        var authoritativeUpdates = new List<KohScoreEngine.ControlUpdate>(pollTargets.Count);
        foreach (var target in pollTargets)
        {
            if (!pollResults.TryGetValue(target.ChallengeId, out var pollResult) ||
                !pollResult.IsAuthoritative)
            {
                logger.LogDebug(
                    "KoH agent poll for challenge {ChallengeId} was not authoritative; preserving current controller.",
                    target.ChallengeId);
                continue;
            }

            authoritativeUpdates.Add(new KohScoreEngine.ControlUpdate(
                target.ChallengeId,
                pollResult.ControllerTeamId));
        }

        if (!await scoreEngine.UpdateControlsAsync(
            competition.Id,
            authoritativeUpdates,
            now,
            controlPoints,
            ct,
            refreshLeaderboard: true,
            validateControllers: true))
        {
            throw new InvalidOperationException("KoH control update lease was unavailable.");
        }
    }

    private static Guid? ResolveControllerTeamId(string? identifier, IReadOnlyDictionary<Guid, string> teams)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;
        if (Guid.TryParse(identifier, out var parsedId) && teams.ContainsKey(parsedId))
            return parsedId;

        var match = teams.FirstOrDefault(team => team.Value == identifier).Key;
        return match == Guid.Empty ? null : match;
    }

    private static Dictionary<int, int> ReadPorts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool HasRuntimeConfiguration(Challenge challenge)
        => (challenge.ContainerMode == ChallengeContainerMode.DockerCompose &&
            !string.IsNullOrWhiteSpace(challenge.ComposeYaml)) ||
           !string.IsNullOrWhiteSpace(challenge.ContainerImage);

    private sealed record PollTarget(Guid ChallengeId, string Host, int Port, string? ApiKey);
    private sealed record PollResult(bool IsAuthoritative, Guid? ControllerTeamId);
}
